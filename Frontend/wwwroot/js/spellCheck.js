// ============================================
// Real-time Spell Checking
// Context-aware spell checking that avoids code
// ============================================

class SpellChecker {
    constructor() {
        this.enabled = true;
        this.technicalTerms = new Set([
            // Programming languages
            'javascript', 'typescript', 'python', 'csharp', 'java', 'cpp', 'golang', 'rust',
            'php', 'ruby', 'swift', 'kotlin', 'scala', 'haskell', 'lua', 'perl',
            
            // Frameworks & Libraries
            'react', 'angular', 'vue', 'svelte', 'nextjs', 'nuxt', 'gatsby',
            'django', 'flask', 'fastapi', 'express', 'nestjs', 'aspnet', 'blazor',
            'tensorflow', 'pytorch', 'keras', 'scikit', 'numpy', 'pandas',
            'jquery', 'bootstrap', 'tailwind', 'webpack', 'vite', 'rollup',
            
            // Technologies
            'nodejs', 'deno', 'bun', 'docker', 'kubernetes', 'terraform',
            'aws', 'azure', 'gcp', 'firebase', 'supabase', 'vercel', 'netlify',
            'mysql', 'postgresql', 'mongodb', 'redis', 'elasticsearch',
            'graphql', 'grpc', 'websocket', 'webrtc', 'oauth', 'jwt',
            
            // Common tech terms
            'api', 'sdk', 'cli', 'gui', 'ide', 'vscode', 'github', 'gitlab',
            'cicd', 'devops', 'frontend', 'backend', 'fullstack', 'microservices',
            'serverless', 'saas', 'paas', 'iaas', 'crud', 'rest', 'json', 'xml',
            'html', 'css', 'scss', 'sass', 'jsx', 'tsx', 'yaml', 'toml',
            
            // AI/ML terms
            'llm', 'gpt', 'bert', 'transformer', 'neural', 'dataset', 'tokenizer',
            'embedding', 'vectordb', 'rag', 'finetuning', 'inference',
            
            // Common abbreviations
            'async', 'await', 'const', 'var', 'func', 'params', 'args', 'props',
            'config', 'env', 'repo', 'regex', 'util', 'impl', 'spec', 'docs'
        ]);
        
        this.codePatterns = [
            /```[\s\S]*?```/g,  // Code blocks
            /`[^`]+`/g,          // Inline code
            /\b[a-z]+\([^)]*\)/g, // Function calls
            /\b[A-Z_]+\b/g,      // Constants
            /\b[a-z]+\.[a-z]+/g, // Object.property
            /\b\w+:\/\//g,       // URLs
            /\b[a-z]+\/[a-z]+/g, // Paths
            /\b0x[0-9a-fA-F]+/g, // Hex numbers
            /\b\d+\.\d+\.\d+/g   // Version numbers
        ];
    }
    
    isCodeContext(text, cursorPosition) {
        // Check if cursor is inside code block or inline code
        const beforeCursor = text.substring(0, cursorPosition);
        const afterCursor = text.substring(cursorPosition);
        
        // Check for code blocks
        const codeBlocksBefore = (beforeCursor.match(/```/g) || []).length;
        if (codeBlocksBefore % 2 === 1) {
            return true; // Inside code block
        }
        
        // Check for inline code
        const inlineCodeBefore = (beforeCursor.match(/`/g) || []).length;
        if (inlineCodeBefore % 2 === 1) {
            return true; // Inside inline code
        }
        
        // Check if surrounded by code patterns
        const surroundingText = text.substring(
            Math.max(0, cursorPosition - 50),
            Math.min(text.length, cursorPosition + 50)
        );
        
        for (const pattern of this.codePatterns) {
            if (pattern.test(surroundingText)) {
                return true;
            }
        }
        
        return false;
    }
    
    isTechnicalTerm(word) {
        return this.technicalTerms.has(word.toLowerCase());
    }
    
    shouldCheckWord(word, text, cursorPosition) {
        if (!word || word.length < 3) return false;
        if (this.isTechnicalTerm(word)) return false;
        if (this.isCodeContext(text, cursorPosition)) return false;
        if (/^[A-Z_]+$/.test(word)) return false; // All caps (likely constant)
        if (/^\d+$/.test(word)) return false; // Numbers
        if (/^[a-z]+[A-Z]/.test(word)) return false; // camelCase
        if (word.includes('_')) return false; // snake_case
        if (word.includes('-')) return false; // kebab-case
        
        return true;
    }
    
    async checkText(text, cursorPosition) {
        if (!this.enabled) return { corrections: [], isCodeContext: false };
        
        const isCode = this.isCodeContext(text, cursorPosition);
        if (isCode) return { corrections: [], isCodeContext: true };
        
        // Extract words around cursor
        const words = text.match(/\b[a-zA-Z]+\b/g) || [];
        const corrections = [];
        
        for (const word of words) {
            if (this.shouldCheckWord(word, text, cursorPosition)) {
                // Use browser's spellcheck API if available
                if (typeof navigator !== 'undefined' && navigator.spellcheck) {
                    const isCorrect = await navigator.spellcheck.check(word);
                    if (!isCorrect) {
                        corrections.push({
                            word: word,
                            suggestions: await navigator.spellcheck.suggest(word)
                        });
                    }
                }
            }
        }
        
        return { corrections, isCodeContext: false };
    }
    
    enable() {
        this.enabled = true;
    }
    
    disable() {
        this.enabled = false;
    }
    
    addTechnicalTerm(term) {
        this.technicalTerms.add(term.toLowerCase());
    }
    
    removeTechnicalTerm(term) {
        this.technicalTerms.delete(term.toLowerCase());
    }
}

// Global instance
window.spellChecker = new SpellChecker();

// Blazor interop functions
window.enableSpellCheck = function() {
    window.spellChecker.enable();
};

window.disableSpellCheck = function() {
    window.spellChecker.disable();
};

window.addTechnicalTerm = function(term) {
    window.spellChecker.addTechnicalTerm(term);
};

window.checkSpelling = async function(text, cursorPosition) {
    return await window.spellChecker.checkText(text, cursorPosition);
};

window.isCodeContext = function(text, cursorPosition) {
    return window.spellChecker.isCodeContext(text, cursorPosition);
};

// Setup spellcheck attribute on textareas
document.addEventListener('DOMContentLoaded', function() {
    const textareas = document.querySelectorAll('.chat-input');
    textareas.forEach(textarea => {
        // Enable browser's native spellcheck but we'll enhance it
        textarea.setAttribute('spellcheck', 'true');
        
        // Add custom handling
        textarea.addEventListener('input', function(e) {
            const cursorPos = this.selectionStart;
            const isCode = window.spellChecker.isCodeContext(this.value, cursorPos);
            
            // Disable native spellcheck in code context
            if (isCode) {
                this.setAttribute('spellcheck', 'false');
            } else {
                this.setAttribute('spellcheck', 'true');
            }
        });
    });
});
