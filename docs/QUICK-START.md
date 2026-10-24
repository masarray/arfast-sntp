# AR Fast SNTP — Quick start

**All you need is your time server's IP address.**

If your PC clock is slow to synchronize, AR Fast SNTP gives you a simple way to connect it to an NTP/SNTP server and see what's happening.

## Windows: install → enter IP → done

1. **[Download AR Fast SNTP for Windows x64](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe)**.
2. Run the installer. When setup asks for your time server, enter its IPv4 address.
3. Open **AR Fast SNTP** from the Start menu. You can see your server and synchronization status directly in Studio.

<img src="../assets/screenshot/Screenshot%202026-10-11%20072931.webp" width="700" alt="AR Fast SNTP dialog for entering your SNTP server IP address">

You can add or remove more servers in the app whenever you want; you don't need to reinstall or restart the service each time.

## Which IP address should I enter?

Use the IPv4 address of a reachable NTP/SNTP time server.

- **Industrial HMI, IPC, or lab:** Use your GPS-based NTP server's IPv4 address.
- **Everyday Windows PC:** Use the IPv4 address published by a trusted public NTP provider, if your network allows access.

The address must belong to an **NTP server**, not simply another computer on the network. Standard NTP client/server communication uses **UDP port 123**. Ask your network administrator for the address when testing on a managed engineering network.

## How do I know it's working?

Look at the status in AR Fast SNTP Studio. You can check which time server is selected, the clock offset, the response time (RTT), and recent synchronization events.

<img src="../assets/screenshot/Screenshot%202026-10-11%20073259.webp" width="800" alt="AR Fast SNTP showing selected NTP server, synchronization values and event history">

AR Fast SNTP keeps monitoring in the background after you close its window.

## If a server says "No reply"

1. Check that you entered the **correct IP address** for an NTP server.
2. Check that your PC can reach that server over the network.
3. Confirm that the server responds to NTP requests on **UDP port 123**.
4. If you are on a corporate or substation network, check with the network administrator that NTP requests are allowed.

For other issues, see [Support and troubleshooting](../SUPPORT.md) or [open an issue](https://github.com/masarray/arfast-sntp/issues/new/choose).

## Linux desktop

A [Linux x64 beta](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1) is available with its own GUI and background service. Follow the [Linux installation guide](LINUX-INSTALL.md).

## More

[Windows installer](https://github.com/masarray/arfast-sntp/releases/latest) · [Project website](https://masarray.github.io/arfast-sntp/) · [Source code](https://github.com/masarray/arfast-sntp)
