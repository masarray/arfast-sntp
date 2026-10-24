#!/bin/sh
set -eu
if [ "$(id -u)" -ne 0 ]; then echo 'Run: sudo sh ./install.sh' >&2; exit 1; fi
HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
for file in "$HERE/worker/ARFastSNTP.Linux" "$HERE/studio/ARFastSNTP.LinuxStudio" "$HERE/arfast-sntp.service" "$HERE/arfast-sntp.desktop"; do
 [ -s "$file" ] || { echo "Missing $file" >&2; exit 1; }
done
command -v systemctl >/dev/null 2>&1 || { echo 'systemd required' >&2; exit 1; }
command -v pkexec >/dev/null 2>&1 || { echo 'polkit pkexec required' >&2; exit 1; }
install -d -m 0755 /opt/arfast-sntp/worker /opt/arfast-sntp/studio /var/lib/arfast-sntp
cp -R "$HERE/worker/." /opt/arfast-sntp/worker/
cp -R "$HERE/studio/." /opt/arfast-sntp/studio/
chmod 0755 /opt/arfast-sntp/worker/ARFastSNTP.Linux /opt/arfast-sntp/studio/ARFastSNTP.LinuxStudio
install -m 0644 "$HERE/arfast-sntp.service" /etc/systemd/system/arfast-sntp.service
install -m 0644 "$HERE/arfast-sntp.desktop" /usr/share/applications/arfast-sntp.desktop
systemctl daemon-reload
systemctl enable --now arfast-sntp.service
echo 'AR Fast SNTP installed. Open Studio, add an IPv4 NTP server, and authorize with polkit.'
