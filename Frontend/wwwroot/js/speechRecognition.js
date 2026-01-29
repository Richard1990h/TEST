// ============================================
// Speech Recognition for Voice-to-Chat
// Web Speech API Integration
// ============================================

class SpeechRecognitionManager {
    constructor() {
        this.recognition = null;
        this.isListening = false;
        this.dotNetHelper = null;
        this.finalTranscript = '';
        this.interimTranscript = '';
        
        this.initRecognition();
    }
    
    initRecognition() {
        // Check browser support
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        
        if (!SpeechRecognition) {
            console.warn('Speech recognition not supported in this browser');
            return;
        }
        
        this.recognition = new SpeechRecognition();
        this.recognition.continuous = true;
        this.recognition.interimResults = true;
        this.recognition.lang = 'en-US';
        this.recognition.maxAlternatives = 1;
        
        this.setupEventHandlers();
    }
    
    setupEventHandlers() {
        if (!this.recognition) return;
        
        this.recognition.onstart = () => {
            console.log('Speech recognition started');
            this.isListening = true;
            this.finalTranscript = '';
            this.interimTranscript = '';
            this.notifyStatusChange('listening');
        };
        
        this.recognition.onresult = (event) => {
            let interim = '';
            
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const transcript = event.results[i][0].transcript;
                
                if (event.results[i].isFinal) {
                    this.finalTranscript += transcript + ' ';
                } else {
                    interim += transcript;
                }
            }
            
            this.interimTranscript = interim;
            this.notifyTranscriptUpdate(this.finalTranscript + interim);
        };
        
        this.recognition.onerror = (event) => {
            console.error('Speech recognition error:', event.error);
            this.isListening = false;
            this.notifyStatusChange('error', event.error);
        };
        
        this.recognition.onend = () => {
            console.log('Speech recognition ended');
            this.isListening = false;
            this.notifyStatusChange('stopped');
            
            // Send final transcript to Blazor
            if (this.finalTranscript.trim()) {
                this.notifyFinalTranscript(this.finalTranscript.trim());
            }
        };
    }
    
    start(dotNetHelper) {
        if (!this.recognition) {
            alert('Speech recognition is not supported in your browser. Please use Chrome, Edge, or Safari.');
            return false;
        }
        
        if (this.isListening) {
            console.warn('Already listening');
            return false;
        }
        
        this.dotNetHelper = dotNetHelper;
        this.finalTranscript = '';
        this.interimTranscript = '';
        
        try {
            this.recognition.start();
            return true;
        } catch (e) {
            console.error('Failed to start recognition:', e);
            return false;
        }
    }
    
    stop() {
        if (!this.recognition || !this.isListening) return;
        
        try {
            this.recognition.stop();
        } catch (e) {
            console.error('Failed to stop recognition:', e);
        }
    }
    
    setLanguage(lang) {
        if (this.recognition) {
            this.recognition.lang = lang;
        }
    }
    
    notifyStatusChange(status, error = null) {
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('OnSpeechStatusChanged', status, error);
        }
    }
    
    notifyTranscriptUpdate(transcript) {
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('OnTranscriptUpdate', transcript);
        }
    }
    
    notifyFinalTranscript(transcript) {
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('OnFinalTranscript', transcript);
        }
    }
}

// Global instance
window.speechRecognitionManager = new SpeechRecognitionManager();

// Blazor interop functions
window.startSpeechRecognition = function(dotNetHelper) {
    return window.speechRecognitionManager.start(dotNetHelper);
};

window.stopSpeechRecognition = function() {
    window.speechRecognitionManager.stop();
};

window.setSpeechLanguage = function(lang) {
    window.speechRecognitionManager.setLanguage(lang);
};

window.isSpeechRecognitionSupported = function() {
    return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
};

// Get available languages for speech recognition
window.getSupportedSpeechLanguages = function() {
    return [
        { code: 'en-US', name: 'English (US)' },
        { code: 'en-GB', name: 'English (UK)' },
        { code: 'es-ES', name: 'Spanish' },
        { code: 'fr-FR', name: 'French' },
        { code: 'de-DE', name: 'German' },
        { code: 'it-IT', name: 'Italian' },
        { code: 'pt-BR', name: 'Portuguese (Brazil)' },
        { code: 'ru-RU', name: 'Russian' },
        { code: 'zh-CN', name: 'Chinese (Simplified)' },
        { code: 'ja-JP', name: 'Japanese' },
        { code: 'ko-KR', name: 'Korean' },
        { code: 'ar-SA', name: 'Arabic' },
        { code: 'hi-IN', name: 'Hindi' }
    ];
};
