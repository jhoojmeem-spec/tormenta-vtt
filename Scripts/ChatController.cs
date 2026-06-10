using Godot;
using System;
using System.Text.RegularExpressions;
using TormentaVTT.Models;
using TormentaVTT.Services;

namespace TormentaVTT.UI
{
    public partial class ChatController : Panel
    {
        private RichTextLabel _chatLog = null!;
        private LineEdit _chatInput = null!;
        private Button _sendButton = null!;
        private DiceParser _diceParser = new();

        /// <summary>Name shown as sender for local messages. Set by Main.cs after player name is known.</summary>
        public string PlayerName { get; set; } = "Jogador";

        public event Func<string, bool>? CommandTriggered;
        /// <summary>Fires when the local user sends OR rolls — for network sync.</summary>
        public event Action<ChatMessage>? MessageSent;

        public override void _Ready()
        {
            _chatLog = GetNode<RichTextLabel>("ChatVBox/ChatLog");
            _chatLog.BbcodeEnabled = true;
            _chatInput = GetNode<LineEdit>("ChatVBox/ChatInputRow/ChatInput");
            _sendButton = GetNode<Button>("ChatVBox/ChatInputRow/SendChatButton");
            _sendButton.Pressed += OnSendPressed;
            _chatInput.TextSubmitted += _ => OnSendPressed();
        }

        private void OnSendPressed()
        {
            var text = _chatInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (text.StartsWith("/"))
                HandleCommand(text);
            else
            {
                var msg = new ChatMessage(PlayerName, text, ChatMessageType.Chat);
                AddMessage(msg);
                MessageSent?.Invoke(msg);
            }

            _chatInput.Text = string.Empty;
        }

        private void HandleCommand(string text)
        {
            if (text.StartsWith("/roll", StringComparison.OrdinalIgnoreCase))
            {
                var expression = text.Substring(5).Trim();
                if (string.IsNullOrEmpty(expression))
                {
                    SystemMessage("Uso: /roll 1d20+7");
                    return;
                }

                var result = _diceParser.Evaluate(expression);
                var rollText = $"{expression} = {result.Total} ({result.Breakdown})";
                var msg = new ChatMessage(PlayerName, rollText, ChatMessageType.Roll);
                AddMessage(msg);
                MessageSent?.Invoke(msg);   // ← sync roll result to all clients
                return;
            }

            if (text.StartsWith("/w ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
            {
                // Whisper — shown locally only, not synced
                var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) { SystemMessage("Uso: /w <nome> <mensagem>"); return; }
                var target  = parts[1];
                var whisper = parts[2];
                var msg = new ChatMessage(PlayerName, $"(para {target}): {whisper}", ChatMessageType.Whisper);
                AddMessage(msg);
                MessageSent?.Invoke(msg);
                return;
            }

            if (CommandTriggered != null && CommandTriggered.Invoke(text))
                return;

            AddMessage(new ChatMessage("Comando", text, ChatMessageType.System));
        }

        private static readonly Regex InlineRollPattern = new(@"\[\[(.*?)\]\]", RegexOptions.Compiled);

        public void AddMessage(ChatMessage message)
        {
            var colorTag = message.Type switch
            {
                ChatMessageType.Roll    => "[color=#ffd700]",
                ChatMessageType.System  => "[color=#aaaaaa]",
                ChatMessageType.Whisper => "[color=#cc88ff]",
                _                       => "[color=#ffffff]"
            };

            var prefix = message.Type switch
            {
                ChatMessageType.Roll    => "🎲 ",
                ChatMessageType.System  => "⚙ ",
                ChatMessageType.Whisper => "🤫 ",
                _                       => ""
            };

            var text      = ApplyInlineRolls(message.Text);
            var formatted = $"{colorTag}[b]{prefix}{message.Sender}[/b] ({message.TimeCode}): {text}[/color]\n";
            _chatLog.AppendText(formatted);
            _chatLog.ScrollToLine(_chatLog.GetLineCount());
        }

        private string ApplyInlineRolls(string text)
        {
            return InlineRollPattern.Replace(text, match =>
            {
                var expression = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(expression)) return match.Value;
                try
                {
                    var result = _diceParser.Evaluate(expression);
                    return $"{result.Total} ({result.Breakdown})";
                }
                catch { return match.Value; }
            });
        }

        public void AddSystemMessage(string text) =>
            AddMessage(new ChatMessage("Sistema", text, ChatMessageType.System));

        public void SystemMessage(string text) => AddSystemMessage(text);
    }
}
