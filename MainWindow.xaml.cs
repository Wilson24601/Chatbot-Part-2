using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AwarenessChatbot
{
    // ==================== Chatbot Engine ====================
    public delegate string ResponseGenerator(string userInput);

    public class ChatbotEngine
    {
        private readonly Dictionary<string, List<string>> _keywordResponses;
        private readonly Random _random = new Random();

        public string UserName { get; set; } = "User";
        public string FavoriteTopic { get; set; } = null;

        private string _lastTopic = null;
        private int _followUpCount = 0;

        public ChatbotEngine()
        {
            _keywordResponses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["password"] = new List<string>
                {
                    "Password safety means creating strong, unique passwords for each account and never reusing them. It is the first line of defense against unauthorized access.",
                    "A strong password uses a mix of uppercase, lowercase, numbers, and symbols, and is at least 12 characters long. Avoid personal information like birthdays.",
                    "Enable two-factor authentication whenever possible – it adds an extra layer of security beyond just your password."
                },
                ["phishing"] = new List<string>
                {
                    "Phishing is a cyberattack where criminals impersonate trusted sources (banks, colleagues) to steal sensitive information like passwords or credit card numbers.",
                    "Phishing often arrives via email or text with urgent language and fake links. Always verify the sender's address before clicking.",
                    "If an email creates urgency ('your account will be closed'), do not click links – go directly to the official website."
                },
                ["privacy"] = new List<string>
                {
                    "Privacy online means controlling what personal information you share and with whom. It protects you from identity theft and unwanted surveillance.",
                    "Review app permissions regularly – many apps ask for more data than they need. Use privacy-focused browser settings.",
                    "Use a VPN on public Wi-Fi to encrypt your traffic and hide your IP address from prying eyes."
                },
                ["scam"] = new List<string>
                {
                    "An online scam is a deceptive scheme designed to trick you into giving money or personal information. Scammers often pretend to be legitimate companies.",
                    "Scammers often impersonate trusted brands. If it sounds too good to be true, it probably is. Never share one-time passwords.",
                    "Report scam calls or texts to your local authorities – reporting helps protect others from the same fraud."
                },
                ["safe browsing"] = new List<string>
                {
                    "Safe browsing means avoiding suspicious websites, keeping your browser updated, and not downloading files from untrusted sources. It prevents malware infections.",
                    "Look for 'https://' and a padlock icon in the address bar before entering any personal info. Avoid clicking on pop-up ads.",
                    "Use ad-blockers and keep your browser extensions updated – they often contain critical security patches."
                },
                ["2fa"] = new List<string>
                {
                    "Two-factor authentication (2FA) requires a second verification step (like a code from an app or SMS) in addition to your password. It greatly reduces account takeover risk.",
                    "Use an authenticator app (Google Authenticator, Authy) instead of SMS – it is more secure and works without a phone signal.",
                    "Always store backup codes safely in case you lose access to your 2FA device. Never share these codes with anyone."
                },
                ["social engineering"] = new List<string>
                {
                    "Social engineering is a manipulation technique that exploits human psychology to gain access to systems, data, or physical locations. It bypasses technical security.",
                    "Attackers manipulate emotions like fear or curiosity. Always verify unexpected requests through a different channel (e.g., call the person directly).",
                    "Never give out passwords or sensitive info over the phone unless you initiated the call. Be wary of 'urgent' requests from your boss or IT."
                }
            };
        }

        public ResponseGenerator UnknownResponseHandler = (input) =>
            "I'm not sure I understand. Could you rephrase or ask about cybersecurity topics like password, phishing, or privacy?";

        public string GetResponse(string userInput, out bool useMemory)
        {
            useMemory = false;
            string lowerInput = userInput.ToLower();

            string sentiment = DetectSentiment(lowerInput);
            if (sentiment != "neutral")
                return GetSentimentBasedResponse(sentiment);

            if (IsFollowUpRequest(lowerInput) && _lastTopic != null)
            {
                _followUpCount++;
                return GetFollowUpResponse(_lastTopic);
            }

            foreach (var kvp in _keywordResponses)
            {
                if (lowerInput.Contains(kvp.Key))
                {
                    _lastTopic = kvp.Key;
                    _followUpCount = 0;
                    string response = GetRandomResponse(kvp.Value);
                    if (lowerInput.Contains("interested in") || lowerInput.Contains("like") && lowerInput.Contains(kvp.Key))
                    {
                        FavoriteTopic = kvp.Key;
                        useMemory = true;
                        response += $" Great! I'll remember that you're interested in {kvp.Key}.";
                    }
                    return response;
                }
            }

            if (lowerInput.Contains("my name is") || lowerInput.Contains("i'm ") && !lowerInput.Contains("worried"))
            {
                string name = ExtractName(lowerInput);
                if (!string.IsNullOrEmpty(name))
                {
                    UserName = name;
                    return $"Nice to meet you, {UserName}! I'll remember that.";
                }
            }

            if (FavoriteTopic != null && (lowerInput.Contains("my interest") || lowerInput.Contains("what i like")))
                return $"You're interested in {FavoriteTopic}. Here's a tip: {GetRandomResponse(_keywordResponses[FavoriteTopic])}";

            return UnknownResponseHandler(lowerInput);
        }

        private string DetectSentiment(string input)
        {
            if (input.Contains("worried") || input.Contains("scared") || input.Contains("nervous") || input.Contains("anxious"))
                return "worried";
            if (input.Contains("frustrated") || input.Contains("annoyed") || input.Contains("confused") || input.Contains("don't understand"))
                return "frustrated";
            if (input.Contains("curious") || input.Contains("interested") || input.Contains("tell me more") || input.Contains("explain"))
                return "curious";
            return "neutral";
        }

        private string GetSentimentBasedResponse(string sentiment)
        {
            switch (sentiment)
            {
                case "worried":
                    return "It's completely understandable to feel worried. Cybersecurity can be overwhelming. Let me share a simple but powerful tip: " +
                           GetRandomResponse(_keywordResponses["password"]);
                case "frustrated":
                    return "I hear you – some security advice can be confusing. Let's break it down. " +
                           GetRandomResponse(_keywordResponses["phishing"]);
                case "curious":
                    return "That's great! Curiosity helps you stay safe. Here's something interesting: " +
                           GetRandomResponse(_keywordResponses["privacy"]);
                default:
                    return "I'm here to help. What would you like to know about online safety?";
            }
        }

        private bool IsFollowUpRequest(string input)
        {
            string[] followPhrases = { "tell me more", "another tip", "more information", "explain more", "continue", "elaborate" };
            return followPhrases.Any(phrase => input.Contains(phrase));
        }

        private string GetFollowUpResponse(string topic)
        {
            if (_keywordResponses.ContainsKey(topic))
            {
                string response = GetRandomResponse(_keywordResponses[topic]);
                if (_followUpCount >= 2)
                    response += " That's all I have on this topic for now. Want to explore another?";
                return response;
            }
            return "Let's talk about a specific cybersecurity topic like passwords or phishing.";
        }

        private string GetRandomResponse(List<string> responses) => responses[_random.Next(responses.Count)];

        private string ExtractName(string input)
        {
            var parts = input.Split(new[] { "my name is ", "i'm " }, StringSplitOptions.None);
            if (parts.Length > 1)
            {
                string name = parts[1].Trim().Split(' ')[0];
                return char.ToUpper(name[0]) + name.Substring(1);
            }
            return null;
        }
    }

    // ==================== WPF Main Window ====================
    public partial class MainWindow : Window
    {
        private readonly ChatbotEngine _chatbot = new ChatbotEngine();
        private readonly SpeechSynthesizer _speaker = new SpeechSynthesizer();
        private bool _isBotResponding = false;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        public MainWindow()
        {
            InitializeComponent();
            ChatListBox.ItemsSource = Messages;
            _speaker.Rate = 0;
            _speaker.Volume = 100;
            _ = SendBotMessageAsync("Hello! I'm your Cybersecurity Awareness Bot. What's your name?");
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e) => await ProcessInput();
        private async void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) await ProcessInput();
        }
        private async void Topic_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button)?.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                await ProcessUserInput(tag);
        }
        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private async Task ProcessInput()
        {
            string input = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(input) || _isBotResponding) return;
            InputBox.Clear();
            await ProcessUserInput(input);
        }

        private async Task ProcessUserInput(string input)
        {
            AddMessage(input, isUser: true);

            if (_chatbot.UserName == "User" && !input.ToLower().Contains("my name is") && !input.ToLower().Contains("i'm"))
            {
                _chatbot.UserName = input;
                await SendBotMessageAsync($"Nice to meet you, {input}! Ask me about passwords, phishing, privacy, or use the buttons.");
                return;
            }

            bool useMemory = false;
            string response = _chatbot.GetResponse(input, out useMemory);
            await SendBotMessageAsync(response);

            if (useMemory && _chatbot.FavoriteTopic != null)
            {
                await SendBotMessageAsync($"As someone interested in {_chatbot.FavoriteTopic}, " +
                    "you might also want to explore two-factor authentication. Want to hear about it?");
            }
        }

        private async Task SendBotMessageAsync(string text)
        {
            _isBotResponding = true;
            DisableInput(true);

            _speaker.SpeakAsync(text);

            var botMsg = new ChatMessage { Text = "", IsUser = false };
            AddMessage(botMsg);

            foreach (char c in text)
            {
                botMsg.Text += c;
                await Task.Delay(40);
                ScrollToBottom();
            }

            while (_speaker.State == SynthesizerState.Speaking)
                await Task.Delay(100);

            _isBotResponding = false;
            DisableInput(false);
            ScrollToBottom();
        }

        private void AddMessage(string text, bool isUser) => AddMessage(new ChatMessage { Text = text, IsUser = isUser });
        private void AddMessage(ChatMessage msg)
        {
            Dispatcher.Invoke(() =>
            {
                Messages.Add(msg);
                ScrollToBottom();
            });
        }

        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ChatListBox.Items.Count > 0)
                    ChatListBox.ScrollIntoView(ChatListBox.Items[^1]);
            }));
        }

        private void DisableInput(bool disable)
        {
            Dispatcher.Invoke(() =>
            {
                InputBox.IsEnabled = !disable;
                SendBtn.IsEnabled = !disable;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _speaker.Dispose();
            base.OnClosed(e);
        }
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        private string _text;
        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }
        public bool IsUser { get; set; }

        public Brush BackgroundColor => IsUser ? Brushes.DarkSlateBlue : Brushes.SteelBlue;
        public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public FontWeight FontWeight => IsUser ? FontWeights.Bold : FontWeights.Normal;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}