using Godot;
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

        public override void _Ready()
        {
            _chatLog = GetNode<RichTextLabel>("ChatVBox/ChatLog");
            _chatLog.BbcodeEnabled = true;
            _chatInput = GetNode<LineEdit>("ChatVBox/ChatInputRow/ChatInput");
            _sendButton = GetNode<Button>("ChatVBox/ChatInputRow/SendChatButton");
            _sendButton.Pressed += OnSendPressed;
        }

        private void OnSendPressed()
        {
            var text = _chatInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            if (text.StartsWith("/"))
            {
                HandleCommand(text);
            }
            else
            {
                AddMessage(new ChatMessage("Jogador", text, ChatMessageType.Chat));
            }

            _chatInput.Text = string.Empty;
        }

        private void HandleCommand(string text)
        {
            if (text.StartsWith("/roll"))
            {
                var expression = text.Substring(5).Trim();
                if (string.IsNullOrEmpty(expression))
                {
                    SystemMessage("Uso: /roll 1d20+7");
                    return;
                }

                var result = _diceParser.Evaluate(expression);
                AddMessage(new ChatMessage("Rolagem", $"{expression} = {result.Total} ({result.Breakdown})", ChatMessageType.Roll));
                return;
            }

            if (text.StartsWith("/init"))
            {
                SystemMessage("Use o botão de iniciativa ou selecione um token primeiro.");
                return;
            }

            if (text.StartsWith("/heal"))
            {
                SystemMessage("Macro /heal ainda será expandida.");
                return;
            }

            AddMessage(new ChatMessage("Comando", text, ChatMessageType.System));
        }

        public void AddMessage(ChatMessage message)
        {
            var prefix = message.Type switch
            {
                ChatMessageType.Roll => "[ROLAGEM] ",
                ChatMessageType.Chat => "[JOGADOR] ",
                ChatMessageType.Whisper => "[SUSSURRO] ",
                _ => string.Empty,
            };

            var formatted = $"[b]{prefix}{message.Sender}[/b] ({message.TimeCode}): {message.Text}\n";
            _chatLog.Text += formatted;
            _chatLog.ScrollToLine(_chatLog.GetLineCount());
        }

        public void AddSystemMessage(string text)
        {
            AddMessage(new ChatMessage("Sistema", text, ChatMessageType.System));
        }

        public void SystemMessage(string text)
        {
            AddSystemMessage(text);
        }
    }
}
