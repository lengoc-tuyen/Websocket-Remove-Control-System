// Snowman Chatbot Script
console.log('✅ chatbot.js loaded');

// Toggle chat window
function toggleChat() {
    const chatWindow = document.getElementById('chat-window');
    console.log('🔄 toggleChat called, chat-window:', chatWindow);
    if (chatWindow) {
        chatWindow.classList.toggle('open');
        console.log('📦 chat-window classes:', chatWindow.className);
    }
}

// Initialize chatbot
document.addEventListener('DOMContentLoaded', () => {    
    console.log('🚀 Chatbot DOMContentLoaded');
    
    // Note: Click event is handled by ui.js in MainWeb/index.html
    // For other pages, add click listener
    const snowmanImg = document.getElementById('snowman-img');
    if (snowmanImg && !snowmanImg.onclick) {
        console.log('✅ Adding click listener to snowman');
        snowmanImg.addEventListener('click', toggleChat);
    }

    // Init bubble animation
    initChatbot();

    // Enter key to send
    const chatInput = document.getElementById('chat-input');
    if (chatInput) {
        chatInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                sendChatUI();
            }
        });
    }
});

// Bubble animation (hiện/ẩn mỗi 5s)
function initChatbot() {
    const bubble = document.getElementById('snowmanBubble');
    if (!bubble) return;

    setInterval(() => {
        bubble.style.opacity = '1';
        bubble.style.transform = 'translateY(0)';

        setTimeout(() => {
            bubble.style.opacity = '0';
            bubble.style.transform = 'translateY(10px)';
        }, 3000);
    }, 5000);
}

// Send message UI
async function sendChatUI() {
    const input = document.getElementById('chat-input');
    const messagesDiv = document.getElementById('chat-messages');
    
    if (!input || !messagesDiv) return;
    
    const text = input.value.trim();
    if (!text) return;

    // Add user message
    addMessage(text, 'user');
    input.value = '';

    // Add typing indicator
    addMessage('...', 'bot', true);

    try {
        // Check if AI function exists
        if (typeof window.chatWithGemini !== 'function') {
            throw new Error('AI not available - Please load InforOfChatBot.js first');
        }

        // Call AI from InforOfChatBot.js
        const response = await window.chatWithGemini(text);
        
        // Remove typing indicator
        const typingMsg = messagesDiv.querySelector('.msg-bot:last-child');
        if (typingMsg && typingMsg.textContent === '...') {
            typingMsg.remove();
        }

        // Add bot response
        addMessage(response, 'bot');
    } catch (error) {
        console.error('❌ Chat error:', error);
        console.error('❌ Error stack:', error.stack);
        
        // Remove typing indicator
        const typingMsg = messagesDiv.querySelector('.msg-bot:last-child');
        if (typingMsg && typingMsg.textContent === '...') {
            typingMsg.remove();
        }

        // Show detailed error
        addMessage('❌ Lỗi: ' + error.message + '\n\nKiểm tra Console (F12) để xem chi tiết! 😔', 'bot');
    }
}

// Add message to chat
function addMessage(text, type, isTyping = false) {
    const messagesDiv = document.getElementById('chat-messages');
    if (!messagesDiv) return;

    const msgDiv = document.createElement('div');
    msgDiv.className = type === 'bot' ? 'msg-bot' : 'msg-user';
    msgDiv.textContent = text;

    if (isTyping) {
        msgDiv.style.opacity = '0.6';
    }

    messagesDiv.appendChild(msgDiv);
    messagesDiv.scrollTop = messagesDiv.scrollHeight;
}
