# Clash Of SL (CSS)
Clash of SL Server (CSS) that is the fully free open source clash of clans private server and it is not affiliated to "Supercell , Oy " .

![cos_logo](https://github.com/skyprolk/Clash-Of-SL/blob/main/cos_logo.png)

## Screenshots
These screenshots are taken from the actual physical Android mobile phone.

![screenshot_1](https://fiverr-res.cloudinary.com/images/t_main1,q_auto,f_auto,q_auto,f_auto/gigs/360231709/original/38fef6a45214520a3e0edf9c40f155db40cb338b/make-a-clash-of-clans-private-server.jpg)

![screenshot_2](https://fiverr-res.cloudinary.com/images/t_main1,q_auto,f_auto,q_auto,f_auto/gigs2/360231709/original/538b6d506f81c95995a304e566f9697e1441e5c6/make-a-clash-of-clans-private-server.jpg)

![screenshot_3](https://fiverr-res.cloudinary.com/images/t_main1,q_auto,f_auto,q_auto,f_auto/gigs3/360231709/original/9de7f252a2b240eb495a318a4309603d211041cd/make-a-clash-of-clans-private-server.jpg)

## Includes
Clash of SL includes tools for various purposes.
- **Clash SL Client (CSC)** - The client-side application for connecting to the Clash SL server. Players use this to interact with the game world.

- **Clash SL Client Patcher (CSCP)** - A tool that allows users to modify or patch the Clash SL client for custom features or enhancements.
  
- **Clash SL File Decryptor (CSFD)** - A utility that decrypts specific game files, enabling access to their contents.
  
- **Clash SL Proxy (CSP)** - A proxy server that facilitates communication between the client and the server, enhancing performance and security.
  
- **Clash SL SC Editor (CSSCE)** - An editor for creating and modifying in-game content, such as custom scenarios or maps.
  
- **Clash SL Server (CSS)** - This is a fully free and open-source Clash of Clans private server. It operates independently and is not affiliated with “Supercell, Oy.” The CSS provides an alternative gaming experience for Clash of Clans enthusiasts.

## Videos
Would you like to watch some videos about Clash of SL?
- [Clash of SL Preview](https://youtu.be/VBjUW7VXnoE)
- [Clash of SL Gameplay](https://www.mediafire.com/file/nzks2cwsbk0btfn/Gameplay_Video_-_480p.mp4/file)

## How?
You can create a coc private server using Clash of SL by following the instructions included in the videos below.
1. [Part #1 - Gameplay](https://youtu.be/z_B_NoJkjfU?si=Qaeo7GQZQOCipjKP)
2. [Part #2 - WiFi Local Server](https://youtu.be/jQA26Xg0vyE?si=LiAcuc27VoGAuG2R)
3. [Part #3 - Internet Over Public Server](https://youtu.be/oW-jivCkq6Q?si=YeVvaiep7h3pXNVe)

## Hire Me
Can't you make a coc private server by watching the above videos? Do not worry! I can do that for you :)
- [CLICK HERE](https://www.fiverr.com/s/DbmmEo) to hire me on Fiverr.

## Useful Links
You may find the following links useful!
- [Clash Of SL 8.709.1v APK](https://www.mediafire.com/download/9elnxhv7mjowed2)

- [Hosts GO Apk](https://play.google.com/store/apps/details?id=dns.hosts.server.change)

- [No-IP Software](https://www.noip.com/download?page=win)

- [Apk Easy Tool Software](https://forum.xda-developers.com/t/tool-windows-apk-easy-tool-v1-59-2-2021-04-03.3333960/)

- [Wamp Server](https://www.wampserver.com/en/)

- [Redis Server](https://redis.io/download)

- [Latest CSS Release](https://github.com/skyprolk/Clash-Of-SL/releases/)

## Server bootstrap script

Running the server requires Mono/.NET tooling, MySQL, Redis and a pre-populated `cssdb` schema. On fresh Debian or Ubuntu machines you can automate the full setup by running:

```bash
git clone https://github.com/skyprolk/Clash-Of-SL.git
cd Clash-Of-SL
sudo ./scripts/setup_css_server.sh
```

The helper installs the required apt packages (`mono-complete`, `msbuild`, `nuget`, `mysql-server`, `redis-server`, `screen`, `unzip`), creates the `cssdb` database with a `CSS/ClashOfSL!2024` account, and imports `Clash SL Server/Tools/CSSdb.sql`. Environment variables let you override the defaults:

```bash
CSS_DB_PASSWORD="MySecret" CSS_DB_NAME="cssdb" CSS_DB_USER="CSS" sudo ./scripts/setup_css_server.sh
```

After the script finishes you can build and launch the server with Mono:

```bash
msbuild "Clash SL Server/Clash SL Server.csproj"
mono "Clash SL Server/bin/Debug/Clash SL Server.exe"
```

## Reinforcement learning friendly battle simulator

If you only need the deterministic battle resolution logic for AI research you
can use the standalone **BattleSim** library located under `BattleSim/`. The
project targets .NET Standard 2.0 and packages the minimum set of types required
to parse base layouts, replay command streams and score the outcome.

```bash
dotnet build BattleSim/ClashOfSL.BattleSim.csproj

# Run the zero-dependency command line sample
dotnet run --project BattleSim.Runner \
  -- --layout BattleSim/Samples/layout.json \
     --stats BattleSim/Samples/stats.json \
     --commands BattleSim/Samples/commands.json
```

Refer to [`BattleSim/README.md`](BattleSim/README.md) for a quick-start guide,
reinforcement learning integration examples, and instructions for publishing a
self-contained executable.
