🧱 PutDoc --- Quick Start Guide
=============================

PutDoc is a Blazor / .NET 8 application.\
This guide walks you through getting it running on **Ubuntu** or **Windows** from a fresh install.

* * * * *

🧩 Prerequisites
----------------

| Tool | Version | Purpose |
| --- | --- | --- |
| **Git** | Latest stable | Clone the repository |
| **.NET SDK** | **8.x** | Build and run the app (includes runtime) |

* * * * *

🚀 Ubuntu Setup
---------------

### 1️⃣ Install Git

sudo apt update\
sudo apt install -y git\
git --version

### 2️⃣ Install .NET 8 SDK

**Option A --- via apt (preferred)**\
sudo apt update\
sudo apt install -y dotnet-sdk-8.0

**Option B --- via Microsoft script (for older distros)**\
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0\
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"\
echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc

Check:\
dotnet --list-sdks

### 3️⃣ Clone and Run

git clone <https://github.com/johnwaynecornell/PutDoc.git>\
cd PutDoc/src/PutDoc\
dotnet restore\
dotnet run

Then open:\
👉 https://localhost:5001 or http://localhost:5000

Optional: trust the HTTPS dev certificate\
dotnet dev-certs https --trust

* * * * *

🪟 Windows Setup
----------------

### 1️⃣ Install Git

winget install --id Git.Git -e

### 2️⃣ Install .NET 8 SDK

winget install --id Microsoft.DotNet.SDK.8 -e\
dotnet --list-sdks

### 3️⃣ Clone and Run

git clone <https://github.com/johnwaynecornell/PutDoc.git>\
cd .\PutDoc\src\PutDoc\\
dotnet restore\
dotnet run

Then open your browser to:\
👉 https://localhost:5001 or http://localhost:5000

Optional (trust HTTPS dev cert):\
dotnet dev-certs https --trust

* * * * *

📦 Optional --- Build a self-contained release
--------------------------------------------

**Ubuntu**\
cd src/PutDoc\
dotnet publish -c Release -o out\
./out/PutDoc

**Windows**\
cd src\PutDoc\
dotnet publish -c Release -o out\
.\out\PutDoc.exe

* * * * *

🧰 Troubleshooting
------------------

| Issue | Fix |
| --- | --- |
| `dotnet: command not found` | Add `$HOME/.dotnet` to your PATH (Linux) |
| `5000 already in use` | Run with custom URLs:\
`dotnet run --urls "http://localhost:5080;https://localhost:5443"` |
| HTTPS warning | Run `dotnet dev-certs https --trust` |

* * * * *

🧭 One-liner runners
--------------------

**Linux**\
git clone <https://github.com/johnwaynecornell/PutDoc.git> && cd PutDoc/src/PutDoc && dotnet restore && dotnet run

**Windows PowerShell**\
git clone <https://github.com/johnwaynecornell/PutDoc.git>; cd .\PutDoc\src\PutDoc; dotnet restore; dotnet run

* * * * *

Happy building! 🪄\
PutDoc will serve locally with live-reload enabled --- edit and watch your updates instantly.

* * * * *
