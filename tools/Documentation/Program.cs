// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RainPoint.Documentation;

/// <summary>Checks production-source XML contracts, including internal declarations omitted by CS1591.</summary>
internal static class Program
	{
	private static int Main (string[] args)
		{
		if (args.Length == 1 && args[0] == "--self-test")
			return SelfTest ();
		if (args.Length == 0)
			throw new ArgumentException ("Supply production source directories.");
		var files = args.SelectMany (path => Directory.EnumerateFiles (path, "*.cs", SearchOption.AllDirectories))
			.Where (path => !path.Split (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any (part => part is "obj" or "bin" or "artifacts"))
			.Distinct (StringComparer.OrdinalIgnoreCase).ToArray ();
		var sources = files.Select (path => (Path: path, Text: File.ReadAllText (path))).ToArray ();
		var errors = Check (sources, out int count);
		foreach (string error in errors)
			Console.Error.WriteLine (error);
		Console.WriteLine ($"XML documentation: {count} declarations in {files.Length} files; {errors.Count} errors.");
		return errors.Count == 0 ? 0 : 1;
		}

	private static SyntaxTokenList Modifiers (MemberDeclarationSyntax node) => node switch
		{
		BaseTypeDeclarationSyntax type => type.Modifiers,
		BaseMethodDeclarationSyntax method => method.Modifiers,
		BasePropertyDeclarationSyntax property => property.Modifiers,
		BaseFieldDeclarationSyntax field => field.Modifiers,
		DelegateDeclarationSyntax declaration => declaration.Modifiers,
		_ => default
		};

	private static bool Required (MemberDeclarationSyntax node)
		{
		SyntaxTokenList modifiers = Modifiers (node);
		return modifiers.Any (SyntaxKind.PublicKeyword) || modifiers.Any (SyntaxKind.ProtectedKeyword)
			|| modifiers.Any (SyntaxKind.InternalKeyword) || node is EnumMemberDeclarationSyntax
			|| node.Parent is InterfaceDeclarationSyntax
			|| node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax && node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax;
		}

	private static string TypeKey (TypeDeclarationSyntax node) => string.Join (".",
		node.AncestorsAndSelf ().Reverse ().Select (part => part switch
			{
			BaseNamespaceDeclarationSyntax ns => ns.Name.ToString (),
			TypeDeclarationSyntax type => type.Identifier.ValueText + "`" + (type.TypeParameterList?.Parameters.Count ?? 0),
			_ => null
			}).Where (part => part != null));

	private static XElement Documentation (MemberDeclarationSyntax node)
		{
		string text = string.Join ("\n", node.GetLeadingTrivia ().Where (trivia => trivia.GetStructure () is DocumentationCommentTriviaSyntax).Select (trivia => trivia.ToFullString ()));
		if (string.IsNullOrWhiteSpace (text))
			return null;
		text = Regex.Replace (text, @"(?m)^\s*/// ?", "");
		text = Regex.Replace (text, @"/\*\*|\*/|(?m)^\s*\* ?", "");
		return XElement.Parse ("<member>" + text + "</member>");
		}

	private static List<string> Check ((string Path, string Text)[] sources, out int count)
		{
		var members = sources.SelectMany (source => CSharpSyntaxTree.ParseText (source.Text,
			new CSharpParseOptions (LanguageVersion.Preview, DocumentationMode.Diagnose), source.Path)
			.GetRoot ().DescendantNodes ().OfType<MemberDeclarationSyntax> ().Where (Required)).ToArray ();
		count = members.Length;
		var documentedTypes = new HashSet<string> (members.OfType<TypeDeclarationSyntax> ()
			.Where (node => node.GetLeadingTrivia ().Any (trivia => trivia.GetStructure () is DocumentationCommentTriviaSyntax))
			.Select (TypeKey));
		var errors = new List<string> ();
		foreach (MemberDeclarationSyntax member in members)
			{
			var position = member.SyntaxTree.GetLineSpan (member.Span);
			void Error (string message) => errors.Add ($"{position.Path}({position.StartLinePosition.Line + 1}): error XMLDOC: {message}");
			XElement doc;
			try
				{
				doc = Documentation (member);
				}
			catch (System.Xml.XmlException error)
				{
				Error ("Malformed documentation: " + error.Message);
				continue;
				}
			if (doc == null && member is TypeDeclarationSyntax partial && partial.Modifiers.Any (SyntaxKind.PartialKeyword) && documentedTypes.Contains (TypeKey (partial)))
				continue;
			if (doc == null)
				{
				Error ("Missing XML documentation.");
				continue;
				}
			if (doc.Element ("inheritdoc") != null)
				continue;
			bool HasText (XElement element) => element != null && (!string.IsNullOrWhiteSpace (element.Value) || element.Descendants ().Any (child => child.Attribute ("cref") != null));
			if (!HasText (doc.Element ("summary")))
				Error ("A nonempty summary is required.");
			var parameters = member switch
				{
				BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
				TypeDeclarationSyntax type => type.ParameterList?.Parameters ?? default,
				DelegateDeclarationSyntax declaration => declaration.ParameterList.Parameters,
				IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
				_ => default
				};
			var typeParameters = member switch
				{
				MethodDeclarationSyntax method => method.TypeParameterList?.Parameters ?? default,
				TypeDeclarationSyntax type => type.TypeParameterList?.Parameters ?? default,
				DelegateDeclarationSyntax declaration => declaration.TypeParameterList?.Parameters ?? default,
				_ => default
				};
			void Named (string element, string[] names)
				{
				foreach (string name in names)
					{
					XElement[] matches = doc.Elements (element).Where (entry => entry.Attribute ("name")?.Value == name).ToArray ();
					if (matches.Length != 1 || !HasText (matches[0]))
						Error ($"Exactly one nonempty {element} description is required for '{name}'.");
					}
				foreach (XElement entry in doc.Elements (element))
					if (!names.Contains (entry.Attribute ("name")?.Value))
						Error ($"Unknown {element} name '{entry.Attribute ("name")?.Value}'.");
				}
			Named ("param", parameters.Select (parameter => parameter.Identifier.ValueText).ToArray ());
			Named ("typeparam", typeParameters.Select (parameter => parameter.Identifier.ValueText).ToArray ());
			TypeSyntax returnType = member is MethodDeclarationSyntax methodDeclaration ? methodDeclaration.ReturnType
				: member is DelegateDeclarationSyntax delegateDeclaration ? delegateDeclaration.ReturnType : null;
			if (returnType != null && returnType.ToString () != "void" && !HasText (doc.Element ("returns")))
				Error ("A nonempty returns description is required.");
			}
		return errors;
		}

	private static int SelfTest ()
		{
		var cases = new (string Name, string Source, bool Valid)[]
			{
			("public", "public class Missing {}", false),
			("internal", "internal class Missing {}", false),
			("parameters and return", "/// <summary>API.</summary>\npublic class C {\n/// <summary>Converts.</summary>\npublic int F(int value) => value; }", false),
			("malformed", "/// <summary>Broken\npublic class C {}", false),
			("complete", "/// <summary>API.</summary>\npublic class C {\n/// <summary>Converts.</summary>\n/// <param name=\"value\">Input.</param>\n/// <returns>Output.</returns>\ninternal int F(int value) => value; }", true),
			("partial", "/// <summary>API.</summary>\npublic partial class C {}\npublic partial class C {}", true),
			("primary constructor", "/// <summary>Value.</summary>\n/// <param name=\"value\">Input.</param>\ninternal class C(int value) {}", true),
			("protected", "/// <summary>API.</summary>\npublic class C { protected void F() {} }", false),
			("inheritance", "/// <summary>API.</summary>\npublic class C {\n/// <inheritdoc/>\npublic override string ToString() => null; }", true)
			};
		foreach (var test in cases)
			if ((Check (new[] { (test.Name, test.Source) }, out _).Count == 0) != test.Valid)
				throw new InvalidOperationException ("Documentation policy self-test failed: " + test.Name);
		Console.WriteLine ($"XML documentation policy: {cases.Length} scenarios passed.");
		return 0;
		}
	}