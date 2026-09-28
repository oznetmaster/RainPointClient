// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RainPointClient.Desktop;

// Only the Windows reference host persists credentials. The client library remains stateless on disk.
internal sealed class CredentialStore
	{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes ("RainPointClient.Desktop.Account.v1");
	private readonly string _path;

	internal CredentialStore (string path) => _path = Path.GetFullPath (path);

	internal static CredentialStore ForCurrentUser () => new (Path.Combine (
		 Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
		 "RainPointClient", "Desktop", "account.dat"));

	internal SavedAccount? Load ()
		{
		byte[] encrypted;
		try
			{
			using FileStream stream = new (_path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (stream.Length is <= 0 or > 65536)
				throw new InvalidDataException ("Invalid saved-account size.");
			using MemoryStream buffer = new ();
			stream.CopyTo (buffer);
			encrypted = buffer.ToArray ();
			}
		catch (FileNotFoundException) { return null; }
		catch (DirectoryNotFoundException) { return null; }

		byte[] clear = ProtectedData.Unprotect (encrypted, Entropy, DataProtectionScope.CurrentUser);
		try
			{
			SavedAccount account = JsonSerializer.Deserialize<SavedAccount> (clear)
				 ?? throw new InvalidDataException ("Invalid saved account.");
			Validate (account);
			return account;
			}
		finally { Array.Clear (clear, 0, clear.Length); }
		}

	internal void Save (SavedAccount account)
		{
		Validate (account);
		byte[] clear = JsonSerializer.SerializeToUtf8Bytes (account);
		byte[] encrypted;
		try
			{
			encrypted = ProtectedData.Protect (clear, Entropy, DataProtectionScope.CurrentUser);
			}
		finally { Array.Clear (clear, 0, clear.Length); }

		Directory.CreateDirectory (Path.GetDirectoryName (_path)!);
		string temporary = _path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			// Only encrypted bytes reach the file system, including the atomic-update temporary file.
			using (FileStream stream = new (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				stream.Write (encrypted, 0, encrypted.Length);
				stream.Flush (true);
				}
			if (File.Exists (_path))
				File.Replace (temporary, _path, null);
			else
				File.Move (temporary, _path);
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}

	internal void Forget ()
		{
		try
			{
			File.Delete (_path);
			}
		catch (DirectoryNotFoundException) { }
		}

	private static void Validate (SavedAccount account)
		{
		if (account.Version != 1 || string.IsNullOrWhiteSpace (account.Email) || account.Email.Length > 1024
			 || string.IsNullOrEmpty (account.Password) || account.Password.Length > 4096
			 || string.IsNullOrEmpty (account.CountryCode) || account.CountryCode.Length > 8
			 || account.CountryCode.Any (character => character is < '0' or > '9'))
			throw new InvalidDataException ("Invalid saved account.");
		}
	}

internal sealed class SavedAccount
	{
	public SavedAccount ()
		{
		}

	[JsonPropertyName ("version"), JsonRequired]
	public int Version { get; set; } = 1;
	[JsonPropertyName ("email"), JsonRequired]
	public string Email { get; set; } = string.Empty;
	[JsonPropertyName ("password"), JsonRequired]
	public string Password { get; set; } = string.Empty;
	[JsonPropertyName ("countryCode"), JsonRequired]
	public string CountryCode { get; set; } = string.Empty;
	[JsonPropertyName ("automaticSignIn"), JsonRequired]
	public bool AutomaticSignIn
		{
		get; set;
		}
	}