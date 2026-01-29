// ============================================
// Enhanced Site.js - Premium UI
// Intelligent assistant functionality
// ============================================
// ============================================
// PROFILE POPOVER CLICK-AWAY HANDLER (GLOBAL)
// ============================================
window.profileClickAway = {
    register: function (dotNetRef) {
        document.addEventListener("click", function (e) {

            // ❌ Ignore avatar clicks
            if (e.target.closest(".profile-btn")) {
                return;
            }

            dotNetRef.invokeMethodAsync("CloseProfileMenu");
        });
    }
};

// Typewriter effect is now handled by typewriter-auto.js
// No manual invocation needed - it's automatic!

// ===== GLOBAL THEME HANDLER =====
window.setTheme = function (theme) {
    if (!theme) theme = "light";
    document.documentElement.setAttribute("data-theme", theme);
};

window.dispatchChatMessage = function (filename, code) {
    const chatBox = document.querySelector('.chat-box');
    if (!chatBox) return;

    const message = document.createElement('div');
    message.className = 'chat-message bot';
    message.innerHTML = `<strong>🛠 Fixed ${filename}</strong><pre>${code}</pre>`;
    chatBox.appendChild(message);

    setTimeout(() => chatBox.scrollTop = chatBox.scrollHeight, 50);
};

// ✅ Blazor helper: scroll a referenced element to the bottom
window.scrollToBottom = function (element) {
    try {
        if (!element) return;
        element.scrollTop = element.scrollHeight;
    } catch {
        // no-op
    }
};

// ✅ Enhanced Markdown Rendering with Better Code Handling
window.renderMarkdown = function (text) {
    if (!text) return "";
    if (typeof marked === 'undefined') {
        console.warn("Marked library not loaded");
        return escapeHtml(text);
    }
    
    try {
        const renderer = new marked.Renderer();
        
        // Enhanced code block rendering with copy button and language badge
        renderer.code = function(code, language) {
            const langDisplay = language || 'text';
            const langClass = language || '';
            const escapedCode = escapeHtml(code);
            
            let highlighted = escapedCode;
            if (typeof hljs !== 'undefined' && language && hljs.getLanguage(language)) {
                try {
                    highlighted = hljs.highlight(code, { language }).value;
                } catch (e) {
                    console.error("Highlight.js error:", e);
                }
            }
            
            const codeId = 'code-' + Math.random().toString(36).substr(2, 9);
            
            return `
                <div class="code-block-wrapper">
                    <div class="code-block-header">
                        <span class="code-language-badge">${langDisplay}</span>
                        <button class="code-copy-btn" onclick="copyCodeToClipboard('${codeId}')" title="Copy code">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                                <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                            </svg>
                            <span class="copy-text">Copy</span>
                        </button>
                    </div>
                    <pre class="code-block"><code id="${codeId}" class="hljs language-${langClass}">${highlighted}</code></pre>
                </div>
            `;
        };

        // Enhanced inline code rendering
        renderer.codespan = function(code) {
            return `<code class="inline-code">${escapeHtml(code)}</code>`;
        };

        // Enhanced link rendering (open in new tab)
        renderer.link = function(href, title, text) {
            const titleAttr = title ? ` title="${escapeHtml(title)}"` : '';
            return `<a href="${escapeHtml(href)}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
        };

        marked.setOptions({
            renderer: renderer,
            breaks: true,
            gfm: true,
            headerIds: false,
            mangle: false
        });

        return marked.parse(text);
    } catch (e) {
        console.error("Markdown parsing error:", e);
        return escapeHtml(text);
    }
};

// ✅ Copy Code to Clipboard with Feedback
window.copyCodeToClipboard = async function(codeId) {
    try {
        const codeElement = document.getElementById(codeId);
        if (!codeElement) return;
        
        const code = codeElement.textContent;
        await navigator.clipboard.writeText(code);
        
        // Visual feedback
        const button = codeElement.closest('.code-block-wrapper').querySelector('.code-copy-btn');
        if (button) {
            const originalHTML = button.innerHTML;
            button.innerHTML = `
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="20 6 9 17 4 12"></polyline>
                </svg>
                <span class="copy-text">Copied!</span>
            `;
            button.classList.add('copied');
            
            setTimeout(() => {
                button.innerHTML = originalHTML;
                button.classList.remove('copied');
            }, 2000);
        }
    } catch (err) {
        console.error('Failed to copy code:', err);
    }
};

// ✅ Apply Syntax Highlighting to All Code Blocks
window.applyHighlighting = function () {
    if (typeof hljs !== 'undefined') {
        document.querySelectorAll('pre code:not(.hljs)').forEach((block) => {
            hljs.highlightElement(block);
        });
    }
};

// ✅ Download file from byte array or base64 string
// This is called by Blazor with either ZIP bytes directly or base64 + mimeType
window.downloadFile = (fileName, data, mimeType) => {
    console.log('[downloadFile] Starting download:', fileName);

    try {
        let blob;

        // Check if data is a base64 string (3rd parameter is mimeType)
        if (typeof data === 'string' && mimeType) {
            // Decode base64
            const byteCharacters = atob(data);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            const uint8Array = new Uint8Array(byteNumbers);
            blob = new Blob([uint8Array], { type: mimeType });
            console.log('[downloadFile] Created blob from base64, size:', blob.size);
        } else {
            // Assume data is a byte array
            const uint8Array = new Uint8Array(data);

            // Log first 4 bytes for debugging
            if (uint8Array.length >= 4) {
                console.log('[downloadFile] First 4 bytes (hex):',
                    uint8Array[0].toString(16).padStart(2, '0'),
                    uint8Array[1].toString(16).padStart(2, '0'),
                    uint8Array[2].toString(16).padStart(2, '0'),
                    uint8Array[3].toString(16).padStart(2, '0'));

                // Check for PK signature (ZIP)
                if (uint8Array[0] === 0x50 && uint8Array[1] === 0x4B) {
                    console.log('[downloadFile] Valid ZIP signature detected');
                }
            }

            blob = new Blob([uint8Array], { type: "application/zip" });
            console.log('[downloadFile] Created blob from bytes, size:', blob.size);
        }

        // Create URL and download
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);

        // Cleanup after a short delay
        setTimeout(() => URL.revokeObjectURL(url), 100);

        console.log('[downloadFile] Download triggered successfully');
    } catch (e) {
        console.error('[downloadFile] Error:', e);
        alert('Download failed: ' + e.message);
    }
};

// ✅ Download project by session ID (for project creation)
// Uses fetch with blob for reliable binary download
window.downloadProject = async function(sessionId, projectName) {
    console.log('[downloadProject] Starting download for session:', sessionId, 'project:', projectName);
    
    if (!sessionId) {
        console.error('[downloadProject] No session ID provided');
        alert('Download failed: No session ID');
        return;
    }

    // Get button reference for UI feedback
    let button = null;
    let originalHTML = null;
    try {
        button = event?.target?.closest('button');
        originalHTML = button?.innerHTML;
        if (button) {
            button.innerHTML = '⏳ Downloading...';
            button.disabled = true;
        }
    } catch (e) {
        // Event might not be available
    }

    try {
        // Fetch the ZIP file from the BACKEND server (not frontend)
        // The backend runs on port 8001, frontend on 50792
        const backendUrl = window.backendApiUrl || 'http://localhost:8001';
        const downloadUrl = `${backendUrl}/api/chat/download-project/${sessionId}`;
        console.log('[downloadProject] Fetching from:', downloadUrl);
        
        const response = await fetch(downloadUrl);
        console.log('[downloadProject] Response status:', response.status, 'type:', response.headers.get('content-type'));
        
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || `HTTP ${response.status}`);
        }

        // Get the blob
        const blob = await response.blob();
        console.log('[downloadProject] Blob received, size:', blob.size, 'type:', blob.type);
        
        // Verify it's a valid size
        if (blob.size < 22) {
            throw new Error('Downloaded file is too small to be a valid ZIP');
        }

        // Create download URL and trigger download
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = (projectName || 'Project') + '.zip';
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        
        // Cleanup
        setTimeout(() => URL.revokeObjectURL(url), 100);

        console.log('[downloadProject] Download triggered successfully');

        // Update button to show success
        if (button) {
            button.innerHTML = '✅ Downloaded!';
            setTimeout(() => {
                if (originalHTML) button.innerHTML = originalHTML;
                button.disabled = false;
            }, 2000);
        }

    } catch (error) {
        console.error('[downloadProject] Error:', error);
        alert('Download failed: ' + error.message);
        
        if (button) {
            button.innerHTML = '❌ Failed - Retry';
            button.disabled = false;
        }
    }
};

// ✅ Escape HTML to prevent XSS
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ✅ Auto-resize textarea as user types
window.autoResizeTextarea = function(textarea) {
    if (!textarea) return;
    textarea.style.height = 'auto';
    textarea.style.height = Math.min(textarea.scrollHeight, 200) + 'px';
};

// ✅ Copy arbitrary text (used by Code Fix bubbles)
window.copyTextRaw = async function(text) {
    try {
        await navigator.clipboard.writeText(text || "");
        return true;
    } catch (e) {
        console.error("copyTextRaw failed", e);
        return false;
    }
};

// ✅ Download arbitrary text as a file
window.downloadTextFile = function(filename, content) {
    try {
        const safeName = filename || "download.txt";
        const blob = new Blob([content || ""], { type: "text/plain;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = safeName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    } catch (e) {
        console.error("downloadTextFile failed", e);
    }
};

// ✅ Initialize on page load
document.addEventListener('DOMContentLoaded', function() {
    // Apply highlighting to any existing code blocks
    applyHighlighting();
    
    // Setup auto-resize for textareas
    const textareas = document.querySelectorAll('.chat-input');
    textareas.forEach(textarea => {
        textarea.addEventListener('input', function() {
            autoResizeTextarea(this);
        });
    });
});

// ✅ Keyboard shortcuts
document.addEventListener('keydown', function(e) {
    // Ctrl/Cmd + K: Clear chat (if implemented)
    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        const clearButton = document.querySelector('.clear-chat-btn');
        if (clearButton) clearButton.click();
    }

    // Escape: Stop generating (if implemented)
    if (e.key === 'Escape') {
        const stopButton = document.querySelector('.stop-generating-btn');
        if (stopButton && stopButton.style.display !== 'none') {
            stopButton.click();
        }
    }
});
