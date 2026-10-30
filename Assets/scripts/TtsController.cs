using PonyuDev.SherpaOnnx.Tts;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

public class TtsController : MonoBehaviour
{
    [SerializeField] private AudioSource _audioSource;

    private ITtsService _tts;

    private async void Awake()
    {
        _tts = new TtsService();
        await _tts.InitializeAsync();
    }

    public async Task<float[]> GenerateTtsAsync(string text)
    {
        var result = await _tts.GenerateAsync(text);
        if (result == null) return null;
        return result.Samples;
    }

    private void OnDestroy()
    {
        _tts?.Dispose();
    }
}