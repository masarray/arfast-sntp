/* Progressive enhancement only. All links have working GitHub "latest"
 * fallbacks, so download never depends on the API, JS, or an API key. */
(() => {
  "use strict";
  const repo = "masarray/arfast-sntp";
  const origin = "https://github.com/" + repo + "/releases/download/";
  const installer = "ARFastSNTP-Setup-Windows-x64.exe";
  const names = { installer, checksum: installer + ".sha256" };
  fetch("https://api.github.com/repos/" + repo + "/releases/latest",
    { headers: { Accept: "application/vnd.github+json" } })
    .then(response => { if (!response.ok) throw new Error("Release API unavailable"); return response.json(); })
    .then(release => {
      if (release.draft || release.prerelease || !/^v\d+\.\d+\.\d+$/.test(release.tag_name)) return;
      const assets = Array.isArray(release.assets) ? release.assets : [];
      for (const node of document.querySelectorAll("[data-release-version]"))
        node.textContent = release.tag_name;
      for (const link of document.querySelectorAll("[data-release-notes]"))
        link.href = "https://github.com/" + repo + "/releases/tag/" + encodeURIComponent(release.tag_name);
      for (const link of document.querySelectorAll("[data-release-asset]")) {
        const name = names[link.dataset.releaseAsset];
        const asset = assets.find(item => item.name === name && typeof item.browser_download_url === "string");
        if (asset && asset.browser_download_url.startsWith(origin) &&
            asset.browser_download_url.endsWith("/" + name)) link.href = asset.browser_download_url;
      }
      const exe = assets.find(item => item.name === installer);
      if (exe && Number.isFinite(exe.size)) {
        const size = (exe.size / (1024 * 1024)).toFixed(1);
        for (const node of document.querySelectorAll("[data-release-size]"))
          node.textContent = "Installer: " + size + " MiB.";
      }
    })
    .catch(() => { /* Keep the permanent server-side latest-release links. */ });
})();
