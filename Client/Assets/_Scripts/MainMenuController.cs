using System;
using System.Net;
using DarkRift;
using DarkRift.Client;
using GameLogicCommon;
using MasterDegree;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    #region Variables

    [SerializeField] private InfoSystem     _InfoSystem;
    [SerializeField] private Button         _CreateGameButton;
    [SerializeField] private Button         _JoinButton;
    [SerializeField] private TMP_InputField _InputField;

    private DrClient _Client;
    private bool     _ButtonBlock;

    private const string _GameServersControllerAddress = "172.25.202.126"; //"134.122.95.19";
    private const int    _GameServersControllerPort    = 31317;

    #endregion Variables

    #region Special Methods

    private void Awake()
    {
        _Client = new DrClient();
        _Client.Connect(IPAddress.Parse(_GameServersControllerAddress), _GameServersControllerPort);
        _Client.MessageReceived += OnMessageReceived;

        if (_Client.Client.ConnectionState != ConnectionState.Connected)
        {
            _InfoSystem.AddInfo("Can't connect to main server :(", 4f);
        }

        _CreateGameButton.onClick.AddListener(CreateGamePressed);
        _JoinButton.onClick.AddListener(JoinButtonPressed);
    }

    private void Update()
    {
        _Client?.Update();
    }

    #endregion Special Methods

    #region Private Methods

    private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        using (Message netMessage = e.GetMessage())
        {
            using (DarkRiftReader reader = netMessage.GetReader())
            {
                switch (e.Tag)
                {
                    case (ushort)Messages.MessageId.AllocatedGameData:
                    {
                        Messages.AllocatedGameData msg = new Messages.AllocatedGameData();
                        msg.Read(reader);

                        if (msg.Port == 0)
                        {
                            _ButtonBlock = false;
                            _InfoSystem.AddInfo("Can't create/join game :(", 2f);
                            break;
                        }

                        GameController.IPAddress = IPAddress.Parse(msg.Address);
                        GameController.Port      = msg.Port;
                        GameController.Code      = msg.Code;

                        SceneManager.LoadScene(1);

                        break;
                    }
                }
            }
        }
    }

    private void CreateGamePressed()
    {
        if (_ButtonBlock) return;
        _ButtonBlock = true;

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            using (Message message = Message.Create((ushort)Messages.MessageId.AllocateGame, writer))
            {
                _Client.Client.SendMessage(message, SendMode.Reliable);
            }
        }
    }

    private void JoinButtonPressed()
    {
        if (_ButtonBlock) return;
        _ButtonBlock = true;
        int code = Int32.Parse(_InputField.text);

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            Messages.GetAllocatedGameData getAllocatedGameData = new Messages.GetAllocatedGameData
            {
                Code = code
            };

            getAllocatedGameData.Write(writer);

            using (Message message = Message.Create((ushort)Messages.MessageId.GetAllocatedGameData, writer))
                _Client.Client.SendMessage(message, SendMode.Reliable);
        }
    }

    #endregion Private Methods
}