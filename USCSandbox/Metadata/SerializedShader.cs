using AssetsTools.NET;
using AssetsTools.NET.Extra.Decompressors.LZ4;
using USCSandbox.Common;
using USCSandbox.Processor;
using UnityVersion = AssetRipper.Primitives.UnityVersion;

namespace USCSandbox.Metadata;

public class SerializedShader
{
    private readonly AssetTypeValueField _shaderBf;
    private readonly UnityVersion _engVer;

    public string Name;
    public string FallbackName;
    public List<string> KeywordNames;
    public List<GPUPlatform> Platforms;
    public byte[] CompressedBlob;
    public List<SerializedSubShader> SubShaders;

    private readonly AssetTypeValueField _offsetsField;
    private readonly AssetTypeValueField _compressedLengthsField;
    private readonly AssetTypeValueField _decompressedLengthsField;

    // not parsing this here for now since it'll only
    // be used once during shader file write time
    public AssetTypeValueField PropsField => _shaderBf["m_ParsedForm"]["m_PropInfo"]["m_Props.Array"];

    public SerializedShader(AssetTypeValueField shaderBf, UnityVersion engVer)
    {
        _shaderBf = shaderBf;
        _engVer = engVer;

        var parsedForm = shaderBf["m_ParsedForm"];
        Name = parsedForm["m_Name"].AsString;
        FallbackName = parsedForm["m_FallbackName"].AsString;
        KeywordNames = parsedForm["m_KeywordNames.Array"]
            .Select(i => i.AsString).ToList();

        Platforms = shaderBf["platforms.Array"]
            .Select(i => (GPUPlatform)i.AsInt).ToList();

        _offsetsField = shaderBf["offsets.Array"];
        _compressedLengthsField = shaderBf["compressedLengths.Array"];
        _decompressedLengthsField = shaderBf["decompressedLengths.Array"];

        CompressedBlob = shaderBf["compressedBlob.Array"].AsByteArray;

        SubShaders = parsedForm["m_SubShaders.Array"]
            .Select(s => new SerializedSubShader(s)).ToList();
    }

    public BlobManager? MakeBlobManager(GPUPlatform platform)
    {
        var platformIndex = Platforms.IndexOf(platform);
        if (platformIndex == -1)
            return null;

        var offsets = SerializedMetadataHelpers.GetArrayValueForPlatform(_offsetsField, platformIndex)
            .Select(o => o.AsUInt).ToList();
        var compressedLengths = SerializedMetadataHelpers.GetArrayValueForPlatform(_compressedLengthsField, platformIndex);
        var decompressedLengths = SerializedMetadataHelpers.GetArrayValueForPlatform(_decompressedLengthsField, platformIndex);
        var compDecompLengths = compressedLengths.Zip(decompressedLengths)
            .Select(p => (p.First.AsUInt, p.Second.AsUInt)).ToList();

        var compStream = new MemoryStream(CompressedBlob);

        var blobs = new byte[compDecompLengths.Count][];
        for (var i = 0; i < compDecompLengths.Count; i++)
        {
            var offset = offsets[i];
            var (compressedLength, decompressedLength) = compDecompLengths[i];

            var decompressedBlob = new byte[decompressedLength];

            var segStream = new SegmentStream(compStream, offset, compressedLength);
            var lz4Decoder = new Lz4DecoderStream(segStream);
            lz4Decoder.Read(decompressedBlob, 0, (int)decompressedLength);
            lz4Decoder.Dispose();

            blobs[i] = decompressedBlob;
        }

        return new BlobManager(blobs, _engVer);
    }
}
