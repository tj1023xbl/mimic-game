using JamesFrowen.SimpleWeb;
using PurrNet;
using System.Collections;
using TMPro;
using UnityEngine;

public class NetworkTestScript : NetworkIdentity
{
    [SerializeField]
    private GameObject playerPrefab;
    [SerializeField]
    private TextMeshProUGUI playerUIText;


    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        GameObject newPlayer = UnityProxy.Instantiate(playerPrefab, new Vector3(0f, 2f), Quaternion.identity);
        NetworkIdentity networkdId = newPlayer.GetComponent<NetworkIdentity>();

        Debug.Log($"Attempting to give ownership of '{newPlayer}' to '{networkdId.localPlayer}'");
        GiveOwnership(networkdId.localPlayer, newPlayer);

        newPlayer.GetComponent<PlayerUI>().SetPromptText(playerUIText);

        //if (!isServer)
            //return;
    }
}
