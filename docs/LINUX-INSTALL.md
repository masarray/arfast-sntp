# AR Fast SNTP for Linux x64

**Time sync made simple on Linux, too.** The Linux beta includes a small desktop app and a background SNTP service.

## Install

Download the `ARFastSNTP-Linux-x64.tar.gz` package from the [Linux release](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1). For a Linux desktop with **systemd** and **polkit (pkexec)**:

```bash
mkdir arfast-linux
tar -xzf ARFastSNTP-Linux-x64.tar.gz -C arfast-linux
cd arfast-linux
sudo sh ./install.sh
```

Open **AR Fast SNTP** in Applications, enter your trusted NTP server IPv4 address, and approve the administrator prompt when adding the server. The service picks up changes automatically.

If you also downloaded the checksum file, verify the download with:

```bash
sha256sum -c ARFastSNTP-Linux-x64.tar.gz.sha256
```

## What you can do

- Enter an NTP/SNTP server IPv4 address from the Linux desktop.
- Synchronize the computer clock through the background service.
- View server connection and synchronization status.
- Manage server entries without manually editing NTP configuration files.

The Linux build is **beta**: try it on a Linux engineering PC or VM first. It has a smaller interface than the Windows Studio.

## Check the service

```bash
systemctl status arfast-sntp
sudo journalctl -u arfast-sntp -n 50 --no-pager
```

The app uses a privileged background service to adjust the system clock. The desktop app itself does not run as root; it asks for system authorization when updating the server list.

## Optional command-line setup

If you're on a headless Linux machine, you can add a server without the desktop UI:

```bash
sudo /opt/arfast-sntp/worker/ARFastSNTP.Linux --add YOUR_SERVER_IPV4
/opt/arfast-sntp/worker/ARFastSNTP.Linux --list
```

Replace `YOUR_SERVER_IPV4` with an actual NTP server address.

**Platform details:** Linux x64, systemd, polkit, IPv4 unicast SNTP. For feedback or questions, [open a GitHub issue](https://github.com/masarray/arfast-sntp/issues/new/choose).

[Back to project overview](../README.md)
