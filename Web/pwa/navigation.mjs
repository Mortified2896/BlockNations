// The lobby keeps its existing HTML and authenticated APIs while Unity remains
// in this document. Messages carry only a match ID, never seats or credentials.
export function installGameNavigation(instance, elements, host = window) {
  const { game, canvas, accountBar, statusPanel, statusMessage, statusLink } = elements;
  const initialStatusLink = statusLink && { href: statusLink.href, textContent: statusLink.textContent };
  let frame = null, opening = null, pendingLobby = false;
  let menu = host.blockNationsMenuReceiver || "";
  let scene = host.blockNationsScene || "";
  const setHistory = view => host.history.pushState({ blocknationsView: view }, "", view === "lobby" ? "/multiplayer" : "/");
  host.history.replaceState({ blocknationsView: "game" }, "", host.location.href);

  function closeLobby(push = true) {
    frame?.remove(); frame = null;
    game.inert = false;
    if (accountBar) accountBar.inert = false;
    if (statusPanel) statusPanel.hidden = true;
    if (statusLink) Object.assign(statusLink, initialStatusLink);
    if (push) setHistory("game");
    canvas.focus();
  }
  function showError() {
    if (!statusPanel) return;
    statusMessage.textContent = "The multiplayer lobby could not load. Check your connection and try again.";
    statusPanel.hidden = false;
    statusLink.href = "/"; statusLink.textContent = "Back to game";
  }
  async function showLobby(push) {
    if (frame) return;
    try {
      const response = await host.fetch("/api/multiplayer/session", { credentials: "same-origin", cache: "no-store" });
      if (response.status === 401 || response.status === 403) {
        host.location.assign("/multiplayer"); return; // Reauthentication needs the top-level browser.
      }
      if (!response.ok || !(await response.json()).ok) throw new Error("Lobby unavailable");
      frame = host.document.createElement("iframe");
      frame.title = "Multiplayer";
      frame.src = "/multiplayer?embedded=1";
      frame.setAttribute("allow", "web-share; clipboard-write");
      Object.assign(frame.style, { position: "fixed", inset: "0", width: "100%", height: "100dvh",
        border: "0", zIndex: "1000", background: "#314d79", boxSizing: "border-box",
        padding: "env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)" });
      // Keep the canvas dimensions stable; a display:none canvas can resize its
      // WebGL framebuffer to zero. The foreground lobby owns input/focus.
      game.inert = true;
      if (accountBar) accountBar.inert = true;
      host.document.body.append(frame);
      if (push) setHistory("lobby");
      frame.focus();
    } catch { showError(); }
  }
  function openLobby(push = true) {
    if (scene !== "MainMenu") {
      if (host.blockNationsExitReceiver) {
        pendingLobby = true;
        instance.SendMessage(host.blockNationsExitReceiver, "QuitToMainMenu");
      } else host.location.assign("/multiplayer");
      return Promise.resolve();
    }
    if (!opening) opening = showLobby(push).finally(() => { opening = null; });
    return opening;
  }
  host.addEventListener("message", event => {
    if (!frame || event.source !== frame.contentWindow || event.origin !== host.location.origin) return;
    if (event.data?.type === "bn-lobby-close") closeLobby();
    else if (event.data?.type === "bn-open-match" && /^[a-f0-9]{32}$/.test(event.data.matchId ?? "") && menu && scene === "MainMenu") {
      const matchId = event.data.matchId;
      closeLobby();
      instance.SendMessage(menu, "OpenWebMatchFromBrowser", matchId);
    }
  });
  host.addEventListener("popstate", event => {
    if (event.state?.blocknationsView === "lobby") openLobby(false);
    else if (frame) closeLobby(false);
  });
  statusLink?.addEventListener("click", event => {
    event.preventDefault();
    if (new URL(statusLink.href, host.location.origin).pathname === "/") closeLobby();
    else openLobby();
  });
  return {
    get isLobbyOpen() { return frame !== null; },
    openLobby,
    registerMenu(target) {
      menu = target;
      if (pendingLobby) { pendingLobby = false; openLobby(false); }
    },
    sceneChanged(name) { scene = name; },
  };
}
if (typeof window !== "undefined") window.installGameNavigation = installGameNavigation;
