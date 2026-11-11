#!/bin/sh
# Offers the built program and the test scripts to a test VM through a short-lived server (see serve.py).
# Usage:  PJ_BIND=<this computer's IP> PJ_VM=<test VM's IP> sh tools/deploy.sh <temp-directory> [seconds=90]
#         (build first: dotnet build -c Release)
# On the VM, in the folder you test in:
#   curl.exe -s -o ProfileJanitor.exe http://<PJ_BIND>:8765/ProfileJanitor.exe
#   curl.exe -s -o tests.zip http://<PJ_BIND>:8765/tests.zip && tar -xf tests.zip
set -e
: "${PJ_BIND:?set PJ_BIND to this computer's IP on the test network}"
: "${PJ_VM:?set PJ_VM to the test VM's IP}"
cd "$(dirname "$0")/.."
mkdir -p "$1"
cp bin/Release/net48/ProfileJanitor.exe "$1/ProfileJanitor.exe"
python -I -c "
import zipfile,sys,os
with zipfile.ZipFile(sys.argv[1]+'/tests.zip','w',zipfile.ZIP_DEFLATED) as f:
    for n in ('tests/vm-test.ps1','tests/run-vm-test.cmd','tests/make-test-profile.ps1','tools/sign.ps1'): f.write(n, os.path.basename(n))
" "$1"
sha256sum "$1/ProfileJanitor.exe"
exec python -I tools/serve.py "$1" "${2:-90}" "$PJ_BIND" "$PJ_VM"
