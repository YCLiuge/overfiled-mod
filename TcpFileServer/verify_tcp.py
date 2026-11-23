"""Real TCP regression checks. Run after dotnet build -c Release; no persistent server."""
import hashlib
import os
from pathlib import Path
import shutil
import socket
import struct
import subprocess
import tempfile
import time

BASE = Path(__file__).resolve().parent
DLL = BASE / 'bin/Release/net6.0/win-x64/TcpFileServer.dll'
passed = []


def check(condition, label):
    assert condition, label
    passed.append(label)
    print('PASS', label, flush=True)


def exact(s, n):
    data = bytearray()
    while len(data) < n:
        part = s.recv(n - len(data))
        if not part:
            raise EOFError(f'expected {n}, received {len(data)}')
        data.extend(part)
    return bytes(data)


def connect(port):
    s = socket.create_connection(('127.0.0.1', port), timeout=4)
    s.settimeout(4)
    return s


def frame(name):
    data = name.encode('utf-8')
    return struct.pack('>i', len(data)) + data


def request(port, name, fragmented=False):
    with connect(port) as s:
        packet = frame(name)
        if fragmented:
            for b in packet:
                s.sendall(bytes([b]))
                time.sleep(.002)
        else:
            s.sendall(packet)
        status, length = struct.unpack('>Bi', exact(s, 5))
        body = exact(s, length)
        check(s.recv(1) == b'', f'connection closes: {name}')
        return status, body


def closed(s):
    try:
        return s.recv(1) == b''
    except (ConnectionResetError, ConnectionAbortedError):
        return True


def start(args, log):
    # Python on Windows uses anonymous stdin pipes; stdout/stderr go to a file.
    return subprocess.Popen(['dotnet', str(DLL), *map(str, args)],
                            stdin=subprocess.PIPE, stdout=log, stderr=log)


def ready(process, port):
    deadline = time.monotonic() + 8
    while time.monotonic() < deadline:
        assert process.poll() is None, 'server exited before listening'
        try:
            with connect(port):
                pass
            time.sleep(.1)
            assert process.poll() is None, 'our server exited after port probe'
            return
        except OSError:
            time.sleep(.05)
    raise AssertionError('server never listened')


def stop(process):
    if process.poll() is None:
        process.stdin.write(b'stop\n')
        process.stdin.flush()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise AssertionError('graceful stop timed out')
    check(process.returncode == 0, 'graceful stop exit code 0')
    process.stdin.close()


with tempfile.TemporaryDirectory(prefix='.tcp-verify-', dir=BASE) as temp:
    temp = Path(temp)
    root = temp / 'root'
    root.mkdir()
    sibling = temp / 'root-sibling'
    sibling.mkdir()
    (sibling / 'secret.txt').write_bytes(b'OUTSIDE SECRET')
    (root / 'hello.txt').write_bytes(b'hello tcp\x00\xff')
    (root / 'empty').touch()
    (root / 'nested').mkdir()
    (root / 'nested' / '\u4e2d\u6587.txt').write_bytes('UTF8 \u4e2d\u6587'.encode())
    large = root / 'large.bin'
    block = bytes(range(256)) * 256
    with large.open('wb') as f:
        for _ in range(512):
            f.write(block)
    expected = hashlib.sha256(large.read_bytes()).digest()
    with (root / 'oversized.bin').open('wb') as f:
        f.truncate(2**31)
    junction = root / 'escape'
    # Junction creation requires no symlink privilege. Target and link stay in this directory.
    result = subprocess.run(['cmd', '/c', 'mklink', '/J', str(junction), str(sibling)],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    assert result.returncode == 0, 'junction creation failed'
    with socket.socket() as reserve:
        reserve.bind(('127.0.0.1', 0))
        port = reserve.getsockname()[1]
    process = None
    try:
        with (temp / 'server.log').open('wb') as log:
            process = start([port, root, 2, 1], log)
            ready(process, port)
            check(request(port, 'hello.txt', True) == (0, b'hello tcp\x00\xff'), 'fragmented legacy request and binary bytes')
            check(request(port, 'empty') == (0, b''), 'zero length file')
            check(request(port, 'nested/\u4e2d\u6587.txt') == (0, 'UTF8 \u4e2d\u6587'.encode()), 'nested UTF8 file')
            check(request(port, 'missing')[0] == 1, 'missing status 1')
            for name in ['../root-sibling/secret.txt', '..\\root-sibling\\secret.txt',
                         str(sibling / 'secret.txt'), 'escape/secret.txt', 'hello.txt:secret', '.', '\\\\?\\C:\\Windows\\win.ini']:
                check(request(port, name)[0] == 2, f'path rejected: {name}')
            check(request(port, 'oversized.bin')[0] == 2, 'signed int32 protocol length limit')
            with connect(port) as s:
                s.sendall(frame('large.bin'))
                status, length = struct.unpack('>Bi', exact(s, 5))
                digest = hashlib.sha256()
                left = length
                while left:
                    chunk = s.recv(min(left, 65536))
                    assert chunk
                    digest.update(chunk)
                    left -= len(chunk)
                check(status == 0 and length == 32 * 1024 * 1024 and digest.digest() == expected,
                      '32 MiB streamed response SHA256 matches')
            for invalid in [struct.pack('>i', 0), struct.pack('>i', 4097), struct.pack('>i', -1), struct.pack('>i', 1) + b'\xff']:
                with connect(port) as s:
                    s.sendall(invalid)
                    status, length = struct.unpack('>Bi', exact(s, 5))
                    exact(s, length)
                    check(status == 2, f'malformed request rejected {invalid!r}')
            with connect(port) as s:
                s.sendall(b'\x00')
                started = time.monotonic()
                check(closed(s) and time.monotonic() - started < 2.5, 'partial header async read timeout')
            with connect(port) as s:
                s.sendall(struct.pack('>i', 20) + b'a')
                started = time.monotonic()
                check(closed(s) and time.monotonic() - started < 2.5, 'partial name async read timeout')
            # Fill both slots with incomplete requests; additional connection must close immediately.
            with connect(port) as a, connect(port) as b:
                a.sendall(b'\x00')
                b.sendall(b'\x00')
                time.sleep(.1)
                with connect(port) as extra:
                    started = time.monotonic()
                    check(closed(extra) and time.monotonic() - started < .8, 'connection cap rejects third peer')
                check(closed(a) and closed(b), 'idle slots reclaimed by deadline')
            # A peer which never drains a large response cannot retain its slot indefinitely.
            stalls = []
            for _ in range(2):
                s = connect(port)
                s.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1024)
                s.sendall(frame('large.bin'))
                stalls.append(s)
            time.sleep(1.8)
            check(request(port, 'hello.txt')[0] == 0, 'blocked async writers timeout and release both slots')
            for s in stalls:
                s.close()
            idle = connect(port)
            idle.sendall(b'\x00')
            stop(process)
            check(closed(idle), 'shutdown closes active connection')
            idle.close()
        # Root itself may not be a junction either.
        with (temp / 'badroot.log').open('wb') as log:
            invalid_root = start([port, junction], log)
            invalid_root.wait(timeout=5)
            check(invalid_root.returncode == 1, 'reparse root rejected at startup')
            invalid_root.stdin.close()
        # No positional arguments verifies the actual default, not a duplicated test constant.
        default_root = DLL.parent / 'Mods'
        made_root = not default_root.exists()
        if made_root:
            default_root.mkdir()
        try:
            with socket.socket() as probe:
                try:
                    probe.bind(('0.0.0.0', 10086))
                    available = True
                except OSError:
                    available = False
            with (temp / 'default.log').open('wb') as log:
                process = start([], log)
                if available:
                    ready(process, 10086)
                    check(request(10086, '__verify_missing__')[0] == 1, 'default port is 10086')
                    stop(process)
                else:
                    process.wait(timeout=5)
                    check(process.returncode == 1, 'default-port collision fails cleanly')
                    process.stdin.close()
                    print('SKIP default-port successful bind: 10086 already belongs to an existing process', flush=True)
        finally:
            if made_root:
                default_root.rmdir()
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=5)
        # rmdir on a junction removes only the link, never the target.
        junction.rmdir()
print(f'ALL {len(passed)} CHECKS PASSED; all test server processes exited', flush=True)
