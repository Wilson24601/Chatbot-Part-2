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
using MySql.Data.MySqlClient;  

namespace AwarenessChatbot
{
    // ============================================================
    // ACTIVITY LOGGER
    // ============================================================
    public static class ActivityLogger
    {
        private static List<string> _log = new List<string>();
        private const int MaxDisplay = 10;

        public static void Log(string action)
        {
            string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {action}";
            _log.Add(entry);
            if (_log.Count > 50) _log.RemoveAt(0);
        }

        public static List<string> GetRecentLogs(int count = MaxDisplay)
        {
            return _log.TakeLast(count).ToList();
        }

        public static void ClearLog() => _log.Clear();
    }

    // ============================================================
    // TASK MANAGER with fallback to in memory storage
    // ============================================================
    public class TaskManager
    {
        
        private readonly string _connectionString = "server=localhost;user=root;password=yourpassword;database=cyberbot;";
        private readonly List<TaskItem> _memoryTasks = new List<TaskItem>(); // fallback storage
        private int _nextId = 1;
        private bool _useDatabase = true;

        public TaskManager()
        {
            // Testing the connection at startup
            _useDatabase = TestConnection();
            if (!_useDatabase)
            {
                ActivityLogger.Log("Database not available – using in‑memory storage.");
            }
        }

        private bool TestConnection()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool AddTask(string title, string description, DateTime? reminder = null)
        {
            if (_useDatabase)
            {
                try
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();
                        string sql = @"INSERT INTO tasks (title, description, reminder_date) 
                                       VALUES (@title, @desc, @reminder)";
                        using (var cmd = new MySqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@title", title);
                            cmd.Parameters.AddWithValue("@desc", description);
                            cmd.Parameters.AddWithValue("@reminder", (object)reminder ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    ActivityLogger.Log($"Task added: '{title}'" + (reminder.HasValue ? $" with reminder on {reminder.Value}" : ""));
                    return true;
                }
                catch (Exception ex)
                {
                    ActivityLogger.Log($"DB error: {ex.Message}. Falling back to memory.");
                    _useDatabase = false; 
                }
            }

            // Fallback store in memory
            _memoryTasks.Add(new TaskItem
            {
                Id = _nextId++,
                Title = title,
                Description = description,
                ReminderDate = reminder,
                IsCompleted = false
            });
            ActivityLogger.Log($"Task added in memory: '{title}'");
            return true;
        }

        public List<TaskItem> GetTasks(bool includeCompleted = false)
        {
            if (_useDatabase)
            {
                try
                {
                    var tasks = new List<TaskItem>();
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();
                        string sql = "SELECT id, title, description, reminder_date, is_completed FROM tasks";
                        if (!includeCompleted) sql += " WHERE is_completed = FALSE";
                        sql += " ORDER BY created_at DESC";
                        using (var cmd = new MySqlCommand(sql, conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                tasks.Add(new TaskItem
                                {
                                    Id = reader.GetInt32(0),
                                    Title = reader.GetString(1),
                                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    ReminderDate = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                                    IsCompleted = reader.GetBoolean(4)
                                });
                            }
                        }
                    }
                    return tasks;
                }
                catch
                {
                    _useDatabase = false;
                }
            }

            // Return from memory
            return _memoryTasks.Where(t => includeCompleted || !t.IsCompleted).ToList();
        }

        public bool CompleteTask(int id)
        {
            if (_useDatabase)
            {
                try
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();
                        string sql = "UPDATE tasks SET is_completed = TRUE WHERE id = @id";
                        using (var cmd = new MySqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    ActivityLogger.Log($"Task ID {id} marked as completed.");
                    return true;
                }
                catch
                {
                    _useDatabase = false;
                }
            }

            // Memory fallback
            var task = _memoryTasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                task.IsCompleted = true;
                ActivityLogger.Log($"Task ID {id} completed in memory.");
                return true;
            }
            return false;
        }

        public bool DeleteTask(int id)
        {
            if (_useDatabase)
            {
                try
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();
                        string sql = "DELETE FROM tasks WHERE id = @id";
                        using (var cmd = new MySqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    ActivityLogger.Log($"Task ID {id} deleted.");
                    return true;
                }
                catch
                {
                    _useDatabase = false;
                }
            }

            // Memory fallback
            var task = _memoryTasks.FirstOrDefault(t => t.Id == id);
            if (task != null)
            {
                _memoryTasks.Remove(task);
                ActivityLogger.Log($"Task ID {id} deleted from memory.");
                return true;
            }
            return false;
        }
    }

    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime? ReminderDate { get; set; }
        public bool IsCompleted { get; set; }

        public override string ToString()
        {
            string status = IsCompleted ? "[X] " : "[ ] ";
            string reminder = ReminderDate.HasValue ? $" (Reminder: {ReminderDate.Value:yyyy-MM-dd})" : "";
            return $"{status}{Title} - {Description}{reminder}";
        }
    }

    // ============================================================
    // QUIZ MANAGER 
    // ============================================================
    public class QuizManager
    {
        private List<QuizQuestion> _questions;
        private int _currentIndex = -1;
        private int _score = 0;
        private bool _isActive = false;

        public QuizManager()
        {
            _questions = new List<QuizQuestion>
            {
                new QuizQuestion("What should you do if you receive an email asking for your password?",
                    new[] { "Reply with your password", "Delete the email", "Report as phishing", "Ignore it" }, 2,
                    "Reporting phishing emails helps prevent scams."),
                new QuizQuestion("Which of these is a strong password?",
                    new[] { "123456", "password", "P@ssw0rd!2024", "qwerty" }, 2,
                    "A strong password uses a mix of uppercase, lowercase, numbers, and symbols."),
                new QuizQuestion("True or False: HTTPS means the website is secure.",
                    new[] { "True", "False" }, 0,
                    "HTTPS encrypts data between you and the website, making it secure."),
                new QuizQuestion("What is phishing?",
                    new[] { "A type of fish", "A cyber attack using fake emails", "A password manager", "A safe browsing tool" }, 1,
                    "Phishing is a cyberattack where criminals impersonate trusted sources."),
                new QuizQuestion("True or False: Two-factor authentication is optional but recommended.",
                    new[] { "True", "False" }, 0,
                    "2FA adds an extra layer of security and is highly recommended."),
                new QuizQuestion("Which is NOT a safe browsing practice?",
                    new[] { "Using HTTPS", "Downloading from unknown sites", "Keeping browser updated", "Using ad-blockers" }, 1,
                    "Downloading from unknown sites can lead to malware."),
                new QuizQuestion("What does VPN stand for?",
                    new[] { "Virtual Private Network", "Very Personal Network", "Visual Programming Node", "Virtual Public Network" }, 0,
                    "VPN stands for Virtual Private Network, which encrypts your internet traffic."),
                new QuizQuestion("True or False: You should use the same password for multiple accounts.",
                    new[] { "True", "False" }, 1,
                    "Using the same password is risky; each account should have a unique password."),
                new QuizQuestion("What is social engineering?",
                    new[] { "A type of engineering", "Manipulating people to reveal confidential info", "Building social networks", "A programming language" }, 1,
                    "Social engineering tricks people into revealing sensitive information."),
                new QuizQuestion("Which is a sign of a scam email?",
                    new[] { "Urgent language", "Spelling errors", "Suspicious sender address", "All of the above" }, 3,
                    "All these are common signs of phishing or scam emails."),
                new QuizQuestion("True or False: Public Wi-Fi is always safe to use.",
                    new[] { "True", "False" }, 1,
                    "Public Wi-Fi is often unencrypted and can be risky; use a VPN."),
                new QuizQuestion("What is the first step in protecting your online privacy?",
                    new[] { "Share your password", "Review privacy settings", "Post everything publicly", "Ignore security updates" }, 1,
                    "Reviewing and adjusting privacy settings is a key first step.")
            };
        }

        public bool IsActive => _isActive;
        public int TotalQuestions => _questions.Count;
        public int CurrentQuestionIndex => _currentIndex + 1;
        public int Score => _score;

        public QuizQuestion GetNextQuestion()
        {
            if (!_isActive)
            {
                _isActive = true;
                _currentIndex = 0;
                _score = 0;
                ActivityLogger.Log("Quiz started.");
                return _questions[0];
            }
            else if (_currentIndex + 1 < _questions.Count)
            {
                _currentIndex++;
                return _questions[_currentIndex];
            }
            else
            {
                _isActive = false;
                ActivityLogger.Log($"Quiz completed. Score: {_score}/{_questions.Count}");
                return null;
            }
        }

        public string SubmitAnswer(int selectedIndex, out bool correct)
        {
            if (_currentIndex < 0 || _currentIndex >= _questions.Count)
            {
                correct = false;
                return "No active question.";
            }
            var q = _questions[_currentIndex];
            correct = selectedIndex == q.CorrectAnswerIndex;
            if (correct) _score++;
            return q.Explanation;
        }

        public string GetFinalMessage()
        {
            double ratio = (double)_score / _questions.Count;
            if (ratio >= 0.9) return "Excellent! You are a cybersecurity pro!";
            else if (ratio >= 0.7) return "Great job! Keep learning to stay safe online!";
            else return "Good effort! Review the topics and try again to improve.";
        }

        public void Reset()
        {
            _isActive = false;
            _currentIndex = -1;
            _score = 0;
        }
    }

    public class QuizQuestion
    {
        public string Question { get; }
        public string[] Options { get; }
        public int CorrectAnswerIndex { get; }
        public string Explanation { get; }

        public QuizQuestion(string q, string[] opts, int correct, string explanation)
        {
            Question = q;
            Options = opts;
            CorrectAnswerIndex = correct;
            Explanation = explanation;
        }
    }

    // ============================================================
    // CHATBOT ENGINE 
    // ============================================================
    public delegate string ResponseGenerator(string userInput);

    public class ChatbotEngine
    {
        private readonly Dictionary<string, List<string>> _keywordResponses;
        private readonly Random _random = new Random();
        private readonly TaskManager _taskManager = new TaskManager();

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

        private enum Intent { None, AddTask, SetReminder, ShowLog, StartQuiz, ViewTasks, CompleteTask, DeleteTask }

        private Intent DetectIntent(string input)
        {
            string l = input.ToLower();
            if (l.Contains("add task") || l.Contains("new task") || l.Contains("create task")) return Intent.AddTask;
            if (l.Contains("remind") || l.Contains("reminder")) return Intent.SetReminder;
            if (l.Contains("show log") || l.Contains("what have you done") || l.Contains("activity log")) return Intent.ShowLog;
            if (l.Contains("quiz") || l.Contains("start quiz") || l.Contains("take quiz")) return Intent.StartQuiz;
            if (l.Contains("view task") || l.Contains("my task") || l.Contains("list task") || l.Contains("show task")) return Intent.ViewTasks;
            if (l.Contains("complete task") || l.Contains("mark complete")) return Intent.CompleteTask;
            if (l.Contains("delete task") || l.Contains("remove task")) return Intent.DeleteTask;
            return Intent.None;
        }

        private string ParseTaskDetails(string input, out string title, out string description, out DateTime? reminder)
        {
            title = "";
            description = "";
            reminder = null;
            string lower = input.ToLower();

            int taskIdx = lower.IndexOf("task");
            if (taskIdx >= 0)
            {
                string afterTask = input.Substring(taskIdx + 4).Trim();
                if (afterTask.StartsWith("to ")) afterTask = afterTask.Substring(3);
                if (afterTask.Contains("remind me")) afterTask = afterTask.Replace("remind me", "").Trim();
                title = afterTask.Trim();
                description = title;
                if (string.IsNullOrEmpty(title))
                {
                    title = "Cybersecurity task";
                    description = "Please specify the task.";
                }
            }
            else if (lower.Contains("remind me"))
            {
                int idx = lower.IndexOf("remind me");
                string after = input.Substring(idx + 9).Trim();
                if (after.StartsWith("to ")) after = after.Substring(3);
                title = after.Trim();
                description = title;
                if (string.IsNullOrEmpty(title))
                {
                    title = "Reminder task";
                    description = "Please specify what to remind.";
                }
            }
            else
            {
                title = input;
                description = input;
            }

            if (lower.Contains("tomorrow"))
                reminder = DateTime.Now.AddDays(1);
            else if (lower.Contains("in "))
            {
                var parts = lower.Split(' ');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == "in" && i + 1 < parts.Length && int.TryParse(parts[i + 1], out int days))
                    {
                        if (i + 2 < parts.Length && (parts[i + 2].Contains("day") || parts[i + 2].Contains("week")))
                        {
                            if (parts[i + 2].Contains("week")) days *= 7;
                            reminder = DateTime.Now.AddDays(days);
                            break;
                        }
                    }
                }
            }
            return "OK";
        }

        public string GetResponse(string userInput, out bool useMemory)
        {
            useMemory = false;
            string lowerInput = userInput.ToLower();

            // Sentiment detection
            string sentiment = DetectSentiment(lowerInput);
            if (sentiment != "neutral")
                return GetSentimentBasedResponse(sentiment);

            // NLP Intent
            Intent intent = DetectIntent(userInput);
            switch (intent)
            {
                case Intent.AddTask:
                    {
                        string title, desc;
                        DateTime? rem;
                        ParseTaskDetails(userInput, out title, out desc, out rem);
                        bool success = _taskManager.AddTask(title, desc, rem);
                        if (success)
                            return $"Task added: '{title}'. {(rem.HasValue ? $"I'll remind you on {rem.Value:yyyy-MM-dd}." : "Would you like to set a reminder?")}";
                        else
                            return "Sorry, I couldn't add the task due to a database issue. Please try again.";
                    }

                case Intent.SetReminder:
                    {
                        string t, d;
                        DateTime? r;
                        ParseTaskDetails(userInput, out t, out d, out r);
                        if (r.HasValue)
                        {
                            bool success = _taskManager.AddTask(t, d, r);
                            if (success)
                                return $"Reminder set for '{t}' on {r.Value:yyyy-MM-dd}.";
                            else
                                return "Sorry, I couldn't set the reminder due to a database issue.";
                        }
                        else
                        {
                            return "Please specify a date for the reminder, e.g., 'Remind me to update password tomorrow.'";
                        }
                    }

                case Intent.ShowLog:
                    {
                        var logs = ActivityLogger.GetRecentLogs();
                        if (logs.Count == 0)
                            return "No activity logged yet.";
                        return "Here is a summary of recent actions:\n" + string.Join("\n", logs.Select((l, i) => $"{i + 1}. {l}"));
                    }

                case Intent.StartQuiz:
                    return "QUIZ_START";

                case Intent.ViewTasks:
                    {
                        var tasks = _taskManager.GetTasks(false);
                        if (tasks.Count == 0)
                            return "You have no pending tasks. Add one with 'Add task ...'";
                        return "Your tasks:\n" + string.Join("\n", tasks.Select((t, i) => $"{i + 1}. {t.ToString()}"));
                    }

                case Intent.CompleteTask:
                    {
                        var words = userInput.Split(' ');
                        int taskId = -1;
                        foreach (var word in words)
                        {
                            if (int.TryParse(word, out taskId))
                                break;
                        }
                        if (taskId > 0)
                        {
                            bool success = _taskManager.CompleteTask(taskId);
                            return success ? $"Task ID {taskId} marked as completed." : "Could not complete task – database error.";
                        }
                        else
                        {
                            return "Please specify the task ID to complete, e.g., 'Complete task 3'.";
                        }
                    }

                case Intent.DeleteTask:
                    {
                        var words = userInput.Split(' ');
                        int taskId = -1;
                        foreach (var word in words)
                        {
                            if (int.TryParse(word, out taskId))
                                break;
                        }
                        if (taskId > 0)
                        {
                            bool success = _taskManager.DeleteTask(taskId);
                            return success ? $"Task ID {taskId} deleted." : "Could not delete task – database error.";
                        }
                        else
                        {
                            return "Please specify the task ID to delete, e.g., 'Delete task 2'.";
                        }
                    }

                default:
                    break;
            }

            // Follow up handling
            if (IsFollowUpRequest(lowerInput) && _lastTopic != null)
            {
                _followUpCount++;
                return GetFollowUpResponse(_lastTopic);
            }

            // Keyword matching
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
                        response += $" Great! I'll remember that you are interested in {kvp.Key}.";
                    }
                    return response;
                }
            }

            // Name capture
            if (lowerInput.Contains("my name is") || lowerInput.Contains("i'm ") && !lowerInput.Contains("worried"))
            {
                string name = ExtractName(lowerInput);
                if (!string.IsNullOrEmpty(name))
                {
                    UserName = name;
                    return $"Nice to meet you, {UserName}! I will remember that.";
                }
            }

            // Memory recall
            if (FavoriteTopic != null && (lowerInput.Contains("my interest") || lowerInput.Contains("what i like")))
                return $"You are interested in {FavoriteTopic}. Here's a tip: {GetRandomResponse(_keywordResponses[FavoriteTopic])}";

            return UnknownResponseHandler(lowerInput);
        }

        // Helper methods
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
                    return "It is completely understandable to feel worried. Cybersecurity can be overwhelming. Let me share a simple but powerful tip: " +
                           GetRandomResponse(_keywordResponses["password"]);
                case "frustrated":
                    return "I hear you – some security advice can be confusing. Let's break it down. " +
                           GetRandomResponse(_keywordResponses["phishing"]);
                case "curious":
                    return "That is great! Curiosity helps you stay safe. Here is something interesting: " +
                           GetRandomResponse(_keywordResponses["privacy"]);
                default:
                    return "I am here to help. What would you like to know about online safety?";
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
                    response += " That is all I have on this topic for now. Want to explore another?";
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

    // ============================================================
    // WPF MAIN WINDOW
    // ============================================================
    public partial class MainWindow : Window
    {
        private readonly ChatbotEngine _chatbot = new ChatbotEngine();
        private readonly SpeechSynthesizer _speaker = new SpeechSynthesizer();
        private bool _isBotResponding = false;
        private bool _speechEnabled = true;
        private QuizManager _quiz = null;
        private bool _awaitingQuizAnswer = false;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        public MainWindow()
        {
            InitializeComponent();
            ChatListBox.ItemsSource = Messages;
            _speaker.Rate = 0;
            _speaker.Volume = 100;
            _ = SendBotMessageAsync("Hello! I am your Cybersecurity Awareness Bot. What is your name?");
            UpdateSpeechButton();
        }

        // ---- Speech Toggle ----
        private void SpeechToggle_Click(object sender, RoutedEventArgs e)
        {
            _speechEnabled = !_speechEnabled;
            UpdateSpeechButton();
            if (_speechEnabled)
                _speaker.SpeakAsync("Speech enabled.");
            else
                _speaker.SpeakAsync("Speech disabled.");
        }

        private void UpdateSpeechButton()
        {
            SpeechToggleBtn.Content = _speechEnabled ? "Speech: On" : "Speech: Off";
            SpeechToggleBtn.Background = _speechEnabled ? new SolidColorBrush(Color.FromRgb(0x5D, 0x6D, 0x7E)) : new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00));
        }

        // ---- Event Handlers ----
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

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;
            string command = tag switch
            {
                "addtask" => "add task",
                "quiz" => "start quiz",
                "showlog" => "show activity log",
                "viewtasks" => "view my tasks",
                _ => ""
            };
            if (!string.IsNullOrEmpty(command))
                await ProcessUserInput(command);
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

            // ---- Quiz answer handling ----
            if (_awaitingQuizAnswer && _quiz != null && _quiz.IsActive)
            {
                int chosen;
                if (int.TryParse(input, out chosen) && chosen >= 1 && chosen <= 4)
                {
                    bool correct;
                    string explanation = _quiz.SubmitAnswer(chosen - 1, out correct);
                    string feedback = correct ? "Correct! " : "Incorrect. ";
                    
                    await SendBotMessageAsync(feedback + explanation, speak: false);

                    var nextQ = _quiz.GetNextQuestion();
                    if (nextQ == null)
                    {
                        string finalMsg = _quiz.GetFinalMessage();
                        await SendBotMessageAsync($"Quiz completed! Your score: {_quiz.Score}/{_quiz.TotalQuestions}. {finalMsg}", speak: false);
                        _awaitingQuizAnswer = false;
                        _quiz = null;
                        return;
                    }
                    else
                    {
                        await DisplayQuizQuestion(nextQ);
                        return;
                    }
                }
                else
                {
                    await SendBotMessageAsync("Please enter a number from 1 to 4 for your answer.", speak: false);
                    return;
                }
            }

            // ---- Name capture  ----
            if (_chatbot.UserName == "User" && !input.ToLower().Contains("my name is") && !input.ToLower().Contains("i'm"))
            {
                _chatbot.UserName = input;
                await SendBotMessageAsync($"Nice to meet you, {input}! Ask me about passwords, phishing, privacy, or use the buttons.");
                return;
            }

            // ---- Normal chatbot processing ----
            bool useMemory = false;
            string response = _chatbot.GetResponse(input, out useMemory);

            if (response == "QUIZ_START")
            {
                _quiz = new QuizManager();
                var firstQ = _quiz.GetNextQuestion();
                if (firstQ != null)
                {
                    _awaitingQuizAnswer = true;
                    await DisplayQuizQuestion(firstQ);
                }
                else
                {
                    await SendBotMessageAsync("Sorry, no quiz questions available.", speak: false);
                }
                return;
            }

            await SendBotMessageAsync(response);

            if (useMemory && _chatbot.FavoriteTopic != null)
            {
                await SendBotMessageAsync($"As someone interested in {_chatbot.FavoriteTopic}, " +
                    "you might also want to explore two-factor authentication. Want to hear about it?");
            }
        }

        // ---- Quiz display ----
        private async Task DisplayQuizQuestion(QuizQuestion q)
        {
            string msg = $"Question {_quiz.CurrentQuestionIndex}/{_quiz.TotalQuestions}:\n{q.Question}\n";
            for (int i = 0; i < q.Options.Length; i++)
            {
                msg += $"{i + 1}. {q.Options[i]}\n";
            }
            msg += "Type the number of your answer.";
            // This message will be spoken (if speech is enabled)
            await SendBotMessageAsync(msg, speak: true);
        }

        // ---- SendBotMessageAsync with optional speech ----
        private async Task SendBotMessageAsync(string text, bool speak = true)
        {
            _isBotResponding = true;
            DisableInput(true);

            // Speak only if speech is enabled AND we want speech for this message
            if (_speechEnabled && speak)
                _speaker.SpeakAsync(text);

            var botMsg = new ChatMessage { Text = "", IsUser = false };
            AddMessage(botMsg);

            // Faster typing when not speaking
            int delay = (speak && _speechEnabled) ? 35 : 15;
            foreach (char c in text)
            {
                botMsg.Text += c;
                await Task.Delay(delay);
                ScrollToBottom();
            }

            // Wait for speech to finish if it was spoken
            if (_speechEnabled && speak)
            {
                while (_speaker.State == SynthesizerState.Speaking)
                    await Task.Delay(100);
            }

            _isBotResponding = false;
            DisableInput(false);
            ScrollToBottom();
        }

        // ---- UI Helpers ----
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

    // ============================================================
    // CHAT MESSAGE MODEL
    // ============================================================
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