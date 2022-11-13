using BepInEx;
using BepInEx.Logging;
using R2API.Utils;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace KickPlayer
{
    [BepInDependency(R2API.R2API.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    [R2APISubmoduleDependency(nameof(CommandHelper))]    
    public class KickPlayerPlugin : BaseUnityPlugin
    {
        public const string PluginAuthor = "lustner";
        public const string PluginName = "Kick Player";
        public const string PluginVersion = "1.0.0";
        public const string PluginGUID = "lustner.KickPlayer";
        private const string KickPlayerHelpText = "Usage: _kick {player_name}\nDescription: Kick player according to given name";
        public static KickPlayerPlugin Instance { get; set; }
        internal static new ManualLogSource Logger { get; set; }

        private static readonly Dictionary<string, ChatCommand> _chatCommands = new List<ChatCommand>()
        {
            new ChatCommand("KICK", KickPlayerHelpText, KickPlayer),            
        }.ToDictionary(rec => rec.Name);

        public void Awake()
        {
            CommandHelper.AddToConsoleWhenReady();
            Instance = this;
            Logger = base.Logger;
            SetupEventHandlers();
        }

        private static string KickPlayer(NetworkUser sender, string[] args)
        {
            if (!sender.hasAuthority)
            {
                return "";
            }

            if (args.Length != 1)
            {
                return KickPlayerHelpText;
            }

            var playerName = args[0];
            NetworkUser player = GetNetUserFromString(playerName);
            if (player == null)
            {
                return "Unable to find player with given name";
            }

            try
            {
                string steamId = player.id.steamId.steamValue.ToString();                                
                Logger.LogInfo($"Trying to kick player with username {player.userName} and steam_id {steamId}");
                sender.CallCmdSendConsoleCommand("kick_steam", new string[] { steamId });                
                return "Player " + playerName + " kicked";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                return "Unable to kick player";
            }
        }

        private static void SetupEventHandlers()
        {
            On.RoR2.Console.RunCmd += Console_RunCmd;
        }

        private static void Console_RunCmd(On.RoR2.Console.orig_RunCmd orig, RoR2.Console self, RoR2.Console.CmdSender sender, string concommandName, List<string> userArgs)
        {
            orig(self, sender, concommandName, userArgs);
            if (!NetworkServer.active || Run.instance == null)
            {
                return;
            }

            if (!concommandName.Equals("say", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            string chatMessage = userArgs.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(chatMessage) || !chatMessage.StartsWith("_"))
            {
                return;
            }
            string[] splitMessage = chatMessage.Split(new char[] { ' ' });
            string chatCommandName = splitMessage.FirstOrDefault().Substring(1); // substring removes leading underscore            
            if (!_chatCommands.TryGetValue(chatCommandName.ToUpperInvariant(), out ChatCommand chatCommand))
            {
                return;
            }

            var remainingParts = splitMessage.Skip(1);
            string playerNameArg = string.Join(string.Empty, remainingParts);
            string[] commandArgs = new string[] { playerNameArg };

            string resultMessage = chatCommand.Handler(sender.networkUser, commandArgs);
            if (!string.IsNullOrWhiteSpace(resultMessage))
            {
                SendChatMessage(resultMessage);
            }
        }

        #region Console Commands
        [ConCommand(commandName = "kick_player", flags = ConVarFlags.ExecuteOnServer, helpText = KickPlayerHelpText)]
        private static void CommandKickPlayer(ConCommandArgs args)
        {
            Logger.LogDebug(KickPlayer(args.sender, args.userArgs.ToArray()));
        }
        #endregion

        #region Helpers
        private static NetworkUser GetNetUserFromString(string playerString)
        {
            if (!string.IsNullOrWhiteSpace(playerString))
            {
                if (int.TryParse(playerString, out int playerIndex))
                {
                    if (playerIndex < NetworkUser.readOnlyInstancesList.Count && playerIndex >= 0)
                    {
                        return NetworkUser.readOnlyInstancesList[playerIndex];
                    }
                    return null;
                }
                else
                {
                    foreach (NetworkUser networkUser in NetworkUser.readOnlyInstancesList)
                    {
                        if (networkUser.userName.Replace(" ", "").Equals(playerString.Replace(" ", ""), StringComparison.InvariantCultureIgnoreCase))
                        {
                            return networkUser;
                        }
                    }
                    return null;
                }
            }
            return null;
        }
        #endregion

        #region Chat Messages
        private static void SendChatMessage(string message)
        {
            Instance.StartCoroutine(SendChatMessageInternal(message));
        }

        private static IEnumerator SendChatMessageInternal(string message)
        {
            yield return new WaitForSeconds(0.1f);
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = message });
        }

        internal class ChatCommand
        {
            public string Name { get; set; }
            public string HelpText { get; set; }
            public Func<NetworkUser, string[], string> Handler { get; set; }

            internal ChatCommand(string name, string helpText, Func<NetworkUser, string[], string> handler)
            {
                Name = name;
                HelpText = helpText;
                Handler = handler;
            }
        }
        #endregion
    }
}
