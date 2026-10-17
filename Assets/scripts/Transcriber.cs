using System.Threading.Tasks;
using UnityEngine;
using Whisper;

public class Transcriber : MonoBehaviour
{
    [SerializeField] WhisperManager whisper;

    public async Task<string> TranscribeAsync(float[] samples, int sampleRate)
    {
        WhisperResult result = await whisper.GetTextAsync(samples, sampleRate, 1); // mono
        if (result == null)
            return null;
        Debug.Log(result.Result);
        return result.Result;
    }
}
