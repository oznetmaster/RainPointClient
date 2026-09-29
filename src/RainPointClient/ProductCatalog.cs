// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient;

/// <summary>Vendor catalog metadata; presence does not establish device support or authorize commands.</summary>
public sealed class RainPointProductCatalog
	{
	/// <summary>
	/// Gets the version string reported for this firmware or catalog record.
	/// </summary>
	[JsonPropertyName ("version"), JsonInclude]
	public long? Version
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor product-model catalog; listing does not imply implemented control support.
	/// </summary>
	[JsonPropertyName ("models"), JsonRequired, JsonInclude] public IReadOnlyList<RainPointProductModel> Models { get; internal set; } = Array.Empty<RainPointProductModel> ();
	}
/// <summary>
/// Describes vendor product metadata without implying implemented control support.
/// </summary>
public sealed class RainPointProductModel
	{
	/// <summary>
	/// Stores the catalog scene-support flags without inferring physical support.
	/// </summary>
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? SceneSupport
		{
		get; set;
		}
	/// <summary>
	/// Gets the vendor's protocol model name.
	/// </summary>
	[JsonPropertyName ("model"), JsonRequired, JsonInclude] public string Model { get; internal set; } = string.Empty;
	/// <summary>
	/// Gets the numeric vendor model code used to distinguish catalog variants.
	/// </summary>
	[JsonPropertyName ("modelCode"), JsonRequired, JsonInclude]
	public int ModelCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor-facing display model name.
	/// </summary>
	[JsonPropertyName ("displayModel"), JsonInclude]
	public string? DisplayModel
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor product code.
	/// </summary>
	[JsonPropertyName ("productCode"), JsonInclude]
	public int? ProductCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor product-category code without inferring control support.
	/// </summary>
	[JsonPropertyName ("productCategory"), JsonInclude]
	public string? Category
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the advertised port count; catalog presence does not establish implemented zone support.
	/// </summary>
	[JsonPropertyName ("portNumber"), JsonInclude]
	public int? PortCount
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor's hub classification.
	/// </summary>
	[JsonPropertyName ("isMainDevice"), JsonInclude]
	public bool? IsHub
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor child-device type code.
	/// </summary>
	[JsonPropertyName ("subDeviceType"), JsonInclude]
	public int? ChildDeviceType
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the advertised data-point metadata, not an automatically generated decoder.
	/// </summary>
	[JsonPropertyName ("dp"), JsonInclude] public IReadOnlyList<RainPointDataPointDefinition> DataPoints { get; internal set; } = Array.Empty<RainPointDataPointDefinition> ();
	}
/// <summary>Numeric codes and type labels are vendor metadata, not inferred decoders.</summary>
public sealed class RainPointDataPointDefinition
	{
	/// <summary>
	/// Gets the vendor data-point identifier.
	/// </summary>
	[JsonPropertyName ("dpId"), JsonInclude]
	public int? Id
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor data-point code.
	/// </summary>
	[JsonPropertyName ("dpCode"), JsonInclude]
	public int? Code
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor data-point type code.
	/// </summary>
	[JsonPropertyName ("dpType"), JsonInclude]
	public int? Type
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor port association.
	/// </summary>
	[JsonPropertyName ("dpPort"), JsonInclude]
	public int? Port
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the advertised field length in bytes.
	/// </summary>
	[JsonPropertyName ("dpLen"), JsonInclude]
	public int? ByteLength
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor flags without interpreting unrecognized bits.
	/// </summary>
	[JsonPropertyName ("dpFlags"), JsonInclude]
	public int? Flags
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor data-type label.
	/// </summary>
	[JsonPropertyName ("dpDataType"), JsonInclude]
	public string? DataType
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor identity label for the data point.
	/// </summary>
	[JsonPropertyName ("identity"), JsonInclude]
	public string? Identity
		{
		get; internal set;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the RainPoint appCode-2 catalog, preserving distinct model-code variants.</summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed product catalog result.</returns>
	/// <exception cref="RainPointException">The product catalog contains invalid or duplicate model variants. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointProductCatalog> GetProductCatalogAsync (CancellationToken cancellationToken = default)
		{
		var catalog = await GetAsync<RainPointProductCatalog> ("app/common/core/productModel", cancellationToken).ConfigureAwait (false);
		if (catalog.Models is null || catalog.Models.Any (m => m is null || string.IsNullOrWhiteSpace (m.Model) || m.ModelCode < 0 || m.PortCount < 0 || m.DataPoints is null || m.DataPoints.Any (d => d is null))
		 || catalog.Models.GroupBy (m => (m.Model, m.ModelCode)).Any (g => g.Count () > 1))
			throw new RainPointException ("The product catalog contains invalid or duplicate model variants.");
		foreach (var model in catalog.Models)
			model.DataPoints = Array.AsReadOnly (model.DataPoints.ToArray ());
		catalog.Models = Array.AsReadOnly (catalog.Models.ToArray ());
		return catalog;
		}
	}