(function () {
  const panels = new Map(); // sessionId -> { root, timer, lastId }

  function escapeHtml(s) {
    return (s || "").replace(/[&<>"']/g, (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])
    );
  }

  function badgeHtml(source, level) {
    const src = (source || "").toLowerCase();
    const lvl = (level || "").toLowerCase();
    return `
      <span class="pa-badge pa-src-${escapeHtml(src)}">${escapeHtml(source)}</span>
      <span class="pa-badge pa-lvl-${escapeHtml(lvl)}">${escapeHtml(level)}</span>
    `;
  }

  async function fetchEvents(sessionId, afterId) {
    const base = window.backendApiUrl || "";
    const url = `${base}/api/chat/project-activity/${encodeURIComponent(sessionId)}${afterId ? `?afterId=${afterId}` : ""}`;
    const res = await fetch(url, { credentials: "include" });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  }

  function ensurePanel(sessionId, anchorEl) {
    let p = panels.get(sessionId);
    if (p) return p;

    const root = document.createElement("div");
    root.className = "pa-panel";
    root.innerHTML = `
      <div class="pa-header">
        <div class="pa-title">Project Activity</div>
        <button class="pa-close" title="Close">✕</button>
      </div>
      <div class="pa-body"></div>
      <div class="pa-footer"><span class="pa-lock">🔒 Session Locked</span></div>
    `;

    const closeBtn = root.querySelector(".pa-close");
    closeBtn.addEventListener("click", () => window.toggleProjectActivity(sessionId, anchorEl));

    document.body.appendChild(root);

    p = { root, timer: null, lastId: 0 };
    panels.set(sessionId, p);

    // Position near anchor
    const r = anchorEl.getBoundingClientRect();
    const top = Math.max(12, r.top + window.scrollY - 8);
    const left = Math.min(window.innerWidth - 420, r.right + window.scrollX + 12);
    root.style.top = `${top}px`;
    root.style.left = `${Math.max(12, left)}px`;

    return p;
  }

  function renderEvents(panel, events) {
    const body = panel.root.querySelector(".pa-body");
    for (const e of events) {
      panel.lastId = Math.max(panel.lastId, e.id || 0);

      const details = e.detailsJson ? `
        <details class="pa-details">
          <summary>Details</summary>
          <pre>${escapeHtml(e.detailsJson)}</pre>
        </details>
      ` : "";

      const row = document.createElement("div");
      row.className = "pa-row";
      row.innerHTML = `
        <div class="pa-row-head">
          <div class="pa-badges">${badgeHtml(e.source, e.level)}</div>
          <div class="pa-phase">${escapeHtml(e.phase || "")}</div>
        </div>
        <div class="pa-msg">${escapeHtml(e.message || "")}</div>
        ${details}
      `;
      body.appendChild(row);
    }

    // Keep scroll pinned to bottom
    body.scrollTop = body.scrollHeight;
  }

  async function startPolling(panel, sessionId) {
    // initial load
    try {
      const initial = await fetchEvents(sessionId, null);
      renderEvents(panel, initial);
    } catch (err) {
      renderEvents(panel, [{ id: panel.lastId + 1, source: "Chat", level: "Warn", phase: "ACTIVITY", message: `Activity fetch failed: ${err.message}` }]);
    }

    panel.timer = setInterval(async () => {
      try {
        const next = await fetchEvents(sessionId, panel.lastId);
        if (next && next.length) renderEvents(panel, next);
      } catch {
        // silent; polling errors should not spam
      }
    }, 1000);
  }

  function stopPolling(panel) {
    if (panel.timer) {
      clearInterval(panel.timer);
      panel.timer = null;
    }
  }

  window.toggleProjectActivity = function (sessionId, anchorEl) {
    if (!sessionId || !anchorEl) return;

    const existing = panels.get(sessionId);
    if (existing) {
      // close
      stopPolling(existing);
      existing.root.remove();
      panels.delete(sessionId);
      return;
    }

    const panel = ensurePanel(sessionId, anchorEl);
    startPolling(panel, sessionId);
  };
})();
