# AR Fast SNTP: experimental Linux x64

For authorized engineering/test PCs only. Not certified for critical SCADA, historian or SOE timing.

## Install
1. Download the Linux prerelease tar.gz and companion SHA-256 checksum (not the stable Windows release).
2. Check integrity with: sha256sum -c ARFastSNTP-Linux-x64.tar.gz.sha256
3. Unpack: mkdir arfast-linux && tar -xzf ARFastSNTP-Linux-x64.tar.gz -C arfast-linux
4. On a systemd-based Linux x64 desktop with polkit (pkexec), run: cd arfast-linux && sudo sh ./install.sh
5. Launch AR Fast SNTP from Applications. Enter an authorized unicast IPv4 NTP server address and approve the polkit authorization. The daemon re-reads the catalog without manual service restarts.

## Inspection
- Service: sudo systemctl status arfast-sntp
- Logs: sudo journalctl -u arfast-sntp -n 100 --no-pager
- Catalog: /var/lib/arfast-sntp/peers.json
- Status: /var/lib/arfast-sntp/status.json
- Headless add: sudo /opt/arfast-sntp/worker/ARFastSNTP.Linux --add 192.0.2.10
- Headless disable: sudo /opt/arfast-sntp/worker/ARFastSNTP.Linux --disable 192.0.2.10

## Limitations
- Linux UI is a new Avalonia implementation, not yet feature-identical to Windows Studio.
- SNTPv4 IPv4 unicast only; not full RFC 5905, NTS, or NTP broadcast reception.
- Three independent agreeing SNTP samples required before any correction.
- daemon uses privileged clock_settime; GUI never runs as root.
- CI verifies build, catalog validation and loopback UDP SNTP fixture. CI NEVER changes the runner clock. A physical Linux/VM validation of clock correction, polkit and unplug/replug recovery is still needed before field claims.
- Experimental prerelease alongside unchanged stable Windows version.
- Uninstall: sudo systemctl disable --now arfast-sntp; sudo rm -f /etc/systemd/system/arfast-sntp.service /usr/share/applications/arfast-sntp.desktop; sudo systemctl daemon-reload. Back up and remove /opt/arfast-sntp and /var/lib/arfast-sntp only when no longer needed.
