using TMPro;
using UnityEngine;

public class PromptHUD : MonoBehaviour
{
    public static PromptHUD Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI promptText;

    private void Awake() => Instance = this;
    public TextMeshProUGUI PromptText => promptText;
}
