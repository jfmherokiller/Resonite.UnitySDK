using FrooxEngine;
using ResoniteLink;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class AudioClipConverter : AssetConverter<StaticAudioClipWrapper, StaticAudioClip, UnityEngine.AudioClip, FrooxEngine.AudioClip>
{
    public override string AssetClass => "AudioClip";
    public override string AssetName => Source.name;

    protected override Message GenerateConversion() => ConvertAudioClip(Source, PostProcessor != null);

    protected override async Task<AssetData> SendConversion(Message message, LinkInterface link)
    {
        switch(message)
        {
            case ImportAudioClipFile file:
                return await link.ImportAudioClip(file);

            case ImportAudioClipRawData rawData:
                return await link.ImportAudioClip(rawData);

            default:
                throw new NotSupportedException("Unsupported conversion message: " + message.GetType());
        }
    }

    protected override ResoniteLink.Component UpdateProvider(Uri assetUrl, IConversionContext context)
    {
        Provider.Data.URL = assetUrl;

        return Provider.CollectData(context);
    }

    public static ResoniteLink.Message ConvertAudioClip(UnityEngine.AudioClip audioClip, bool requiresPostProcessing)
    {
        if (!requiresPostProcessing)
        {
            // First try to import it as a file. This is easiest and will preserve most data
            // Rather than just extracting the raw pixels
            var assetPath = EditorAssetUtility.GetAssetPath(audioClip);

            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                assetPath = Path.GetFullPath(assetPath);

                if (File.Exists(assetPath) && AssetConversionHelper.IsAudioFileSupportedByResonite(assetPath))
                    return new ImportAudioClipFile() { FilePath = assetPath };
            }
        }

        // Fallback - read the raw audio data
        var import = new ImportAudioClipRawData();

        import.ChannelCount = audioClip.channels;
        import.SampleCount = audioClip.samples;
        import.SampleRate = audioClip.frequency;

        var rawData = import.AccessRawData();

        var dataArray = new float[rawData.Length];

        Action revert = null;

        // We can't read audio data from streaming audio clips directly, this will throw an exception
        // Convert them temporarily to decompressed first, so we can access the data and extract it
        if (audioClip.loadType == AudioClipLoadType.Streaming)
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(audioClip);
            var importer = UnityEditor.AssetImporter.GetAtPath(path);

            switch(importer)
            {
                case UnityEditor.AudioImporter audioImporter:
                    var settings = audioImporter.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    audioImporter.defaultSampleSettings = settings;

                    audioImporter.SaveAndReimport();

                    revert = () =>
                    {
                        var settings = audioImporter.defaultSampleSettings;
                        settings.loadType = AudioClipLoadType.Streaming;
                        audioImporter.defaultSampleSettings = settings;

                        audioImporter.SaveAndReimport();
                    };
                    break;

                default:
                    throw new NotImplementedException($"Unsupported importer type: {importer?.GetType().FullName}");
            }
#else
            throw new NotSupportedException($"Streaming audio clip {audioClip} can only be converted in the editor");
#endif
        }

        if (!audioClip.GetData(dataArray, 0))
            throw new Exception($"Failed to read audio data from {audioClip}. Channels: {import.ChannelCount}, " +
                $"SampleCount: {import.SampleCount}, SampleRate: {import.SampleRate}");

        dataArray.AsSpan().CopyTo(rawData);

        // Revert any changes made to the asset during the import
        revert?.Invoke();

        return import;
    }
}
