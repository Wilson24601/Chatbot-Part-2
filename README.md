# Cybersecurity Awareness Chatbot – WPF Edition

## Changes from Part 1 to Part 2

### Part 1 (Console Application)
- Ran entirely in a command prompt window.
- Used `SpeechSynthesizer` for voice output.
- Displayed ASCII art logo and used a typing effect (`Thread.Sleep`).
- Allowed users to type topic numbers or names.
- Responses were fixed (no random variation).
- No memory of user name or favourite topic.
- No sentiment detection.
- No follow‑up handling (each input was treated independently).
- Basic error handling (default case in switch).

### Part 2 (WPF Application)
- **Graphical User Interface** built with WPF (windows, buttons, chat bubbles).
- Same voice and typing effect, now inside a modern chat window.
- **Keyword recognition** using a `Dictionary<string, List<string>>`.
- **Random responses** – each keyword has a list of replies; one is chosen randomly.
- **Memory** – stores user name and favourite topic; recalls them later.
- **Sentiment detection** – recognises worried, frustrated, or curious and responds empathetically.
- **Conversation flow** – handles follow‑up phrases like “tell me more”, “another tip” without restarting.
- **Topic buttons** – users can click instead of typing.
- **Improved error handling** – default delegate response, no crashes.
- **Code optimisation** – OOP with separate `ChatbotEngine` class, delegate, collections.
