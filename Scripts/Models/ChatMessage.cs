using System;

namespace TormentaVTT.Models
{
    public enum ChatMessageType
    {
        System,
        Chat,
        Roll,
        Whisper
    }

    public sealed class ChatMessage
    {
        public string Sender { get; set; }
        public string Text { get; set; }
        public ChatMessageType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ChatMessage(string sender, string text, ChatMessageType type)
        {
            Sender = sender;
            Text = text;
            Type = type;
        }

        public string TimeCode => Timestamp.ToString("HH:mm");
    }
}
