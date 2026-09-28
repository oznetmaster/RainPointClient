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
	[JsonPropertyName ("version"), JsonInclude]
	public long? Version
		{
		get; internal set;
		}
	[JsonPropertyName ("models"), JsonRequired, JsonInclude] public IReadOnlyList<RainPointProductModel> Models { get; internal set; } = Array.Empty<RainPointProductModel> ();
	}
public sealed class RainPointProductModel
	{
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? SceneSupport
		{
		get; set;
		}
	[JsonPropertyName ("model"), JsonRequired, JsonInclude] public string Model { get; internal set; } = string.Empty;
	[JsonPropertyName ("modelCode"), JsonRequired, JsonInclude]
	public int ModelCode
		{
		get; internal set;
		}
	[JsonPropertyName ("displayModel"), JsonInclude]
	public string? DisplayModel
		{
		get; internal set;
		}
	[JsonPropertyName ("productCode"), JsonInclude]
	public int? ProductCode
		{
		get; internal set;
		}
	[JsonPropertyName ("productCategory"), JsonInclude]
	public string? Category
		{
		get; internal set;
		}
	[JsonPropertyName ("portNumber"), JsonInclude]
	public int? PortCount
		{
		get; internal set;
		}
	[JsonPropertyName ("isMainDevice"), JsonInclude]
	public bool? IsHub
		{
		get; internal set;
		}
	[JsonPropertyName ("subDeviceType"), JsonInclude]
	public int? ChildDeviceType
		{
		get; internal set;
		}
	[JsonPropertyName ("dp"), JsonInclude] public IReadOnlyList<RainPointDataPointDefinition> DataPoints { get; internal set; } = Array.Empty<RainPointDataPointDefinition> ();
	}
/// <summary>Numeric codes and type labels are vendor metadata, not inferred decoders.</summary>
public sealed class RainPointDataPointDefinition
	{
	[JsonPropertyName ("dpId"), JsonInclude]
	public int? Id
		{
		get; internal set;
		}
	[JsonPropertyName ("dpCode"), JsonInclude]
	public int? Code
		{
		get; internal set;
		}
	[JsonPropertyName ("dpType"), JsonInclude]
	public int? Type
		{
		get; internal set;
		}
	[JsonPropertyName ("dpPort"), JsonInclude]
	public int? Port
		{
		get; internal set;
		}
	[JsonPropertyName ("dpLen"), JsonInclude]
	public int? ByteLength
		{
		get; internal set;
		}
	[JsonPropertyName ("dpFlags"), JsonInclude]
	public int? Flags
		{
		get; internal set;
		}
	[JsonPropertyName ("dpDataType"), JsonInclude]
	public string? DataType
		{
		get; internal set;
		}
	[JsonPropertyName ("identity"), JsonInclude]
	public string? Identity
		{
		get; internal set;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the RainPoint appCode-2 catalog, preserving distinct model-code variants.</summary>
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