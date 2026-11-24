using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Utils;
using UnityEngine;

namespace Game.UI {
    /// <summary>
    /// Manages a collection of timed messages for fanfares and error messages
    /// </summary>
    public class FanfareHandler {
        private readonly List<(string, float)> messages = new();
        private readonly StringBuilder messageBuilder = new();
        private string lastDisplayedText;
        private readonly FanfareMessage messageDisplay;
        private readonly float messageTimeout;
        private readonly int lineHeightPercent;

        public FanfareHandler(FanfareMessage messageDisplay, float messageTimeout, int lineHeightPercent) {
            this.messageDisplay = messageDisplay;
            this.messageTimeout = messageTimeout;
            this.lineHeightPercent = lineHeightPercent;
        }

        public bool HasMessages => messages.Count > 0;

        public void Clear() {
            lastDisplayedText = null;
            messages.Clear();
            if (messageDisplay != null) {
                messageDisplay.Hide();
            }
        }

        public void AddMessage(string msg) {
            if (msg == null) { return; }
            msg = msg.Trim();
            if (messages.Count > 0) {
                for (int i = messages.Count - 1; i >= 0; i--) {
                    (string, float) message = messages[i];
                    if (message.Item1 == msg) {
                        messages.RemoveAt(i);
                        break;
                    }
                }
            }
            messages.Add((msg, messageTimeout));
        }

        public void Update(float dt) {
            var expire = !messageDisplay.IsHovering;
            var messageCount = messages.Count;
            if (messageCount == 0) { return; }
            if (UIShowing.MainMenu) {
                if (lastDisplayedText != null) {
                    lastDisplayedText = null;
                    messageDisplay.Hide();
                }
                return;
            }

            if (messageCount == 1) {
                var message = messages[0];
                if (expire) {
                    message.Item2 -= dt;
                    messages[0] = message;
                }
                var isRTTimeout = message.Item2 <= 0f;
                if (isRTTimeout) {
                    messages.Clear();
                    lastDisplayedText = null;
                    messageDisplay.Hide();
                } else {
                    if (lastDisplayedText != message.Item1) {
                        lastDisplayedText = message.Item1;
                        messageDisplay.Show(Texts.WithLineHeight(message.Item1, lineHeightPercent));
                    }
                    UpdateFill();
                }
                return;
            }

            // More expensive situation - multiple messages
            messageBuilder.Clear();
            int lineNum = 0;
            for (int i = messages.Count - 1; i >= 0; i--) {
                (string, float) message = messages[i];
                if (expire) {
                    message.Item2 -= dt;
                    messages[i] = message;
                }
                var isRTTimeout = message.Item2 <= 0f;
                if (isRTTimeout) {
                    messages.RemoveAt(i);
                } else {
                    switch (lineNum) {
                        case 0: messageBuilder.AppendLine(Texts.WithLineHeight(message.Item1, lineHeightPercent)); break;
                        case 1: {
                                if (!messageDisplay.IsHovering) {
                                    messageBuilder.Append("<alpha=#AA>");
                                }
                                messageBuilder.AppendLine(Texts.WithLineHeight(message.Item1, lineHeightPercent));
                                break;
                            }
                        case 2: {
                                if (!messageDisplay.IsHovering) {
                                    messageBuilder.Append("<alpha=#66>");
                                }
                                messageBuilder.Append(Texts.WithLineHeight(message.Item1, lineHeightPercent));
                                break;
                            }
                        default: {
                            if (messageDisplay.IsHovering) {
                                messageBuilder.AppendLine();
                                messageBuilder.Append(Texts.WithLineHeight(message.Item1, lineHeightPercent));
                            }
                            break;
                        }
                    }
                    lineNum++;
                }
            }

            var newText = messageBuilder.ToString();
            if (string.IsNullOrEmpty(newText)) {
                newText = null;
            }
            UpdateFill();
            if (newText != lastDisplayedText) {
                lastDisplayedText = newText;
                if (newText != null) {
                    messageDisplay.Show(newText);
                } else {
                    messageDisplay.Hide();
                }
            }
        }

        private void UpdateFill() {
            if (messages.Count > 0) {
                float timeLeft = messages[^1].Item2;
                messageDisplay.SetFillPercent(timeLeft / messageTimeout);
            }
        }
    }
}
