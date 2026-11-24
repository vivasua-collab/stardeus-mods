// #define DEBUG_TWITCH
using System;
using Game.Constants;
using Game.UI;
using Game.Systems.UIUX;
using KL.Signals;
using KL.Utils;
using UnityEngine;
using WebSocketSharp;
using WebSocket = WebSocketSharp.WebSocket;
using WebSocketState = WebSocketSharp.WebSocketState;

// heavily modified version of
// https://github.com/ppartida/unitwitch
namespace Game.Systems.Twitch {
    public class TwitchClient : IDisposable {
        private const int MaxChars = 500;
        private const string PING = "PING";
        private const string PONG = "PONG";
        private const string JOIN = "JOIN";
        private const string PART = "PART";
        private const string URL = "wss://irc-ws.chat.twitch.tv:443/";
        // private const string URL = "ws://irc-ws.chat.twitch.tv:80/";
        public readonly Signal1<TwitchMessage> OnCommand = new(nameof(OnCommand));
        public readonly Signal1<string> OnError = new(nameof(OnError));
        public readonly Signal1<string> OnOpen = new(nameof(OnOpen));
        public readonly Signal1<string> OnClose = new(nameof(OnClose));
        public readonly Signal1<string> OnJoin = new(nameof(OnJoin));
        public readonly Signal1<string> OnPart = new(nameof(OnPart));
        public Signal0 OnReconnect = new(nameof(OnReconnect));
        private TwitchConfig config;
        public TwitchSettings Settings;
        private WebSocket ws;
        private bool hasErrors = false;
        public bool IsConfigured => !hasErrors;
        public bool HasAuthFailed;
        public bool IsConnectConfirmed;
        public WebSocketState State => ws == null ? WebSocketState.Open : ws.ReadyState;
        public bool IsConnected => ws != null && ws.ReadyState == WebSocketState.Open;

        /*
        Example join message:
        :nickname!nickname@nickname.tmi.twitch.tv JOIN #your_channel
         */

        public TwitchClient(TwitchConfig config, bool isTestClient) {
            this.config = config;
            Application.quitting += () => {
                Destroy();
            };
            // D.Warn("config.oauth: {0}", config.OAuth);
            if (String.IsNullOrWhiteSpace(config.OAuth)) {
                D.Err("Twitch OAuth is blank");
                hasErrors = true;
            }
            if (String.IsNullOrWhiteSpace(config.BotName)) {
                D.Err("Twitch BotName is blank");
                hasErrors = true;
            }
            if (String.IsNullOrWhiteSpace(config.ChannelName)) {
                D.Err("Twitch ChannelName is blank");
                hasErrors = true;
            }
            if (hasErrors) { return; }

            #if DEBUG_TWITCH
            D.Log("Starting Twitch integration");
            D.Warn("Will connect to {0}", URL);
            #endif

            using (ws = new WebSocket(URL, "irc")) {
                ws.Log.Level = LogLevel.Error;
                ws.Log.Output = (logData , str) => {
                    D.Warn("WebSocket: {0}: {1}", str, logData);
                };
                ws.EmitOnPing = true;
                ws.OnMessage += (sender, e) => ReceiveMessage(e.Data);
                ws.OnClose += (sender, e) => {
                    D.Warn("Twitch Connection closed");
                    OnClose.Send(e.Reason);
                };
                ws.OnError += (sender, e) => {
                    D.Err("Connection error: {0}", e.Message);
                    if (isTestClient) {
                        HasAuthFailed = true;
                    }
                    OnError.Send(e.Message);
                };
                ws.OnOpen += (sender, e) => {
                    D.Warn("Twitch Connection opened");
                    OnOpen.Send(e.ToString());
                };
            }
            // D.Warn("Created websocket: {0}", ws);
        }

        public void ExecuteCommand(string command) {
            ws.Send(command);
        }
        public void SendChannelMessage(string message, params object[] args) {
            SendChannelMessage(string.Format(message, args));
        }

        public void SendRaw(string ircMessage) {
            SendChannelMessage(ircMessage);
        }

        public void SendSystemMessage(string message) {
            if (hasErrors || !IsConnected) {
                D.Err("Can't send twitch system message: {0}", message);
                return;
            }
            var msg = $"PRIVMSG #{config.ChannelName} :/me {message}";
            // D.Log("Sending to chat: {0}", msg);
            ws.Send(msg);
        }

        public void SendChannelReply(string message, string replyToId) {
            if (hasErrors || !IsConnected) { return; }
            var msg = $"@reply-parent-msg-id={replyToId} PRIVMSG #{config.ChannelName} :{message}";
            #if DEBUG_TWITCH
            D.Log("Sending reply to chat: {0}", msg);
            #endif
            ws.Send(msg);
        }
        public void SendChannelMessage(string message) {
            if (hasErrors || !IsConnected) { return; }
            var msg = $"PRIVMSG #{config.ChannelName} :{message}";
            #if DEBUG_TWITCH
            D.Log("Sending to chat: {0}", msg);
            #endif
            ws.Send(msg);
        }

        /// <summary>
        /// Twitch does not allow whispers through IRC api. Thanks to all the fucking spammers.
        /// </summary>
        public void SendWhisper(string username, string message) {
            if (hasErrors || !IsConnected) { return; }
            var msg = $"PRIVMSG #{config.ChannelName} :/w {username} {message}";
            D.Log("Sending whisper to {0}: {1}", username, message);
            ws.Send(msg);
        }

        public void ReplyTo(TwitchMessage msg, string reply) {
            if (reply.Contains('\n')) {
                D.Err("Twitch messages cannot contain line breaks! {0}", reply);
                reply = string.Join("; ", reply.Split('\n'));
            }
            if (reply.Length > MaxChars) {
                D.Err("Twitch message is too long! Max 500 characters, current: {0}: {1}",
                    reply.Length, reply);
                reply = reply.Substring(0, MaxChars);
            } else {
                D.Warn("Message length: {0}| {1}", reply.Length, reply);
            }
            if (msg.IsChannelMessage) {
                SendChannelReply(reply, msg.MessageId);
            } else {
                // SendWhisper(msg.Username, reply);
            }
        }
        public void ReplyTo(TwitchMessage msg, string reply, params object[] args) {
            if (msg.IsChannelMessage) {
                SendChannelReply(string.Format(reply, args), msg.MessageId);
            } else {
                // SendWhisper(msg.Username, string.Format(reply, args));
            }
        }

        private bool Reconnect() {
            if (hasErrors) {
                D.Err("Trying to reconnect with errors");
                return false;
            }

            #if DEBUG_TWITCH
            D.Log("Reconnecting to Twitch! Socket state: {0}", ws.ReadyState);
            #endif

            ws.WaitTime = new TimeSpan(0, 0, 1);
            try {
                D.Warn("Before connect");
                ws.Connect();
                D.Warn("After connect");
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed connecting to Twitch");
                hasErrors = true;
                return false;
            }
            OnReconnect.Send();

            if (ws.ReadyState == WebSocketState.Open) {
                D.Warn("Socket open");
                ws.Send("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
                ws.Send("PASS " + config.OAuth);
                ws.Send("NICK " + config.BotName);
                ws.Send("JOIN #" + config.ChannelName);
            } else {
                D.Err("Twitch connection not ready: {0}", ws.ReadyState);
                D.Log("Twitch Connection NOT Ready.");
                return false;
            }
            if (Settings.AnnounceConnect) {
                SendSystemMessage("Stardeus: connected");
            }
            return true;
        }
        private void ReceiveMessage(string msg) {
            #if DEBUG_TWITCH
            D.Log(msg);
            #endif
            if (!IsConnectConfirmed && msg.Contains("authentication failed")) {
                HasAuthFailed = true;
                hasErrors = true;
                ShowAuthErrorPopup();
                if (ws != null) {
                    ws.Close();
                    ws = null;
                }
                return;
            }
            if (hasErrors) {
                D.Warn("Has errors, ignoring twitch message: {0}", msg);
                return;
            }
            try {
                if (msg.StartsWith(PING)) {
                    ws.Send(PONG);
                    return;
                }
                if (msg[0] == ':' && (msg.Contains(JOIN) || msg.Contains(PART))) {
                    if (!IsConnectConfirmed) {
                        D.Warn("Confirming twitch connection: {0}", msg);
                        IsConnectConfirmed = true;
                    }
                    ParseJoinsParts(msg);
                    return;
                }
                if (!msg.Contains(":!")) {
                    return;
                }
                var twMsg = new TwitchMessage(msg);
                D.Warn("Got message: {0}", twMsg);
                if (twMsg.IsCommand) {
                    OnCommand.Send(twMsg);
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Error when handling received message from twitch: {0}", msg);
            }
        }

        public static void ShowAuthErrorPopup() {
            WPopup.Create(true, IconId.CWarning, T.TwitchError, "Twitch bot authentication failed. Please check bot username and OAuth token.").Spawn();
        }

        private void ParseJoinsParts(string msg) {
            foreach (var joinPart in msg.Split('\n')) {
                var idx = joinPart.IndexOf('!');
                if (idx > 1) {
                    var nick = joinPart.Substring(1, joinPart.IndexOf('!') - 1);
                    if (msg.Contains(PART)) {
                        OnPart.Send(nick);
                    } else if (msg.Contains(JOIN)) {
                        OnJoin.Send(nick);
                    }
                }
            }
        }

        private bool hasDisplayedAuthErrorPopup;

        public void ReconnectIfNeeded() {
            if (HasAuthFailed) {
                if (!hasDisplayedAuthErrorPopup) {
                    ShowAuthErrorPopup();
                    hasDisplayedAuthErrorPopup = true;
                }
                return;
            }
            if (hasErrors) {
                D.Err("Trying to reconnect twitch while having errors");
                return;
            }
            if (!IsConnected) {
                Reconnect();
            }
        }

        public void Destroy() {
            if (hasErrors) { return; }
            if (IsConnected) {
                if (Settings.AnnounceConnect) {
                    SendSystemMessage("Stardeus: disconnected");
                }
            }
            D.Warn("Destroying twitch connection");
            ws?.Close();
        }

        public void Dispose() {
            Destroy();
        }
    }
}
