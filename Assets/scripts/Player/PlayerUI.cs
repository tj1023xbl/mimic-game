using UnityEngine;
using TMPro;
using System.Reflection.Metadata;
using System.Data.Common;
using PurrNet;
using PurrNet.Packing;

public class PlayerUI : NetworkBehaviour
{
    [SerializeField]
    private TextMeshProUGUI _promptText = null;

    protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isOwner || _promptText != null)
            return;

        _promptText = PromptHUD.Instance.PromptText;
        GameObject textObj = GameObject.FindGameObjectWithTag("PlayerUIText");

        if (textObj != null && textObj.TryGetComponent<TextMeshProUGUI>(out var tmpObj)) 
        {
            _promptText = tmpObj;
        }
    }

    public void SetPromptText(TextMeshProUGUI promptText) 
    {
        _promptText = promptText;
    }

    // Update is called once per frame
    public void UpdateText(string promptMessage)
    {
        if(_promptText != null)
            _promptText.text = promptMessage;
    }
}
