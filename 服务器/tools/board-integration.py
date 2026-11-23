"""Real loopback TCP verification. Starts/stops only its own isolated server process."""
import argparse, hashlib, json, pathlib, socket, subprocess, time

class Client:
    def __init__(self, port):
        self.s = socket.create_connection(('127.0.0.1', port), 2)
        self.buffer = b''
        self.events = []
        self.welcome = self.read()
        assert self.welcome[0] == 'WELCOME'
        self.seq = 0
    def read(self, timeout=2):
        self.s.settimeout(timeout)
        while b'\n' not in self.buffer:
            data = self.s.recv(65536)
            if not data: raise EOFError('connection closed')
            self.buffer += data
        line, self.buffer = self.buffer.split(b'\n', 1)
        cmd, _, raw = line.decode().partition(' ')
        return cmd, json.loads(raw) if raw else {}
    def request(self, cmd, **payload):
        self.seq += 1
        payload['id'] = self.seq
        self.s.sendall((cmd + ' ' + json.dumps(payload, ensure_ascii=False, separators=(',', ':')) + '\n').encode())
        while True:
            reply = self.read()
            if reply[1].get('id') == self.seq: return reply
            self.events.append(reply)
    def hello(self, nick):
        cmd, p = self.request('HELLO', nick=nick)
        assert cmd == 'OK', (cmd, p)
        self.id = p['sessionId']
    def drain(self):
        events, self.events = self.events, []
        while True:
            try: events.append(self.read(.08))
            except socket.timeout: return events
    def close(self): self.s.close()

class Server:
    def __init__(self, dll, extra):
        with socket.socket() as s:
            s.bind(('127.0.0.1', 0)); self.port = s.getsockname()[1]
        self.log = open(str(dll) + '.board-test.log', 'w', encoding='utf-8')
        self.p = subprocess.Popen(['dotnet', str(dll), '--host', '127.0.0.1', '--port', str(self.port), '--heartbeat', '0', *extra], stdout=self.log, stderr=subprocess.STDOUT)
        self.clients = []
        until = time.monotonic() + 10
        while True:
            if self.p.poll() is not None: raise RuntimeError('Server exited; see log')
            try:
                c = Client(self.port); self.clients.append(c); break
            except OSError:
                if time.monotonic() > until: raise
                time.sleep(.05)
    def client(self, nick=None):
        c = Client(self.port); self.clients.append(c)
        if nick: c.hello(nick)
        return c
    def close(self):
        for c in self.clients: c.close()
        self.p.terminate(); self.p.wait(timeout=5); self.log.close()

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('dll', type=pathlib.Path); ap.add_argument('--probe', action='store_true'); ap.add_argument('--out', type=pathlib.Path)
    args = ap.parse_args(); dll = args.dll.resolve(); checks = []
    result = {'dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'checks': checks}
    def check(name, condition):
        assert condition, name
        checks.append(name); print('PASS', name, flush=True)
    server = Server(dll, [])
    try:
        a = server.client('board_a'); b = server.client('board_b'); c = server.client('board_c'); outsider = server.client('outside')
        outsider.request('LEAVE', channel='general'); outsider.request('JOIN', channel='other')
        reply = a.request('BOARD', channel='general', text='probe')
        result['boardProbe'] = reply
        if args.probe:
            print(json.dumps(result, ensure_ascii=False, indent=2)); return
        check('BOARD recognized in published assembly', reply == ('OK', {'id': a.seq, 'delivered': 2}))
        messages = [x for x in b.drain() if x[0] == 'BOARDMSG']
        check('server-issued sender identity', len(messages) == 1 and messages[0][1]['fromId'] == a.id and messages[0][1]['from'] == 'board_a')
        check('third client receives broadcast', len([x for x in c.drain() if x[0] == 'BOARDMSG']) == 1)
        check('no self echo or chat fallback', not any(x[0] in ('BOARDMSG','MSG') for x in a.drain()))
        check('cross-channel isolation', not any(x[0] in ('BOARDMSG','MSG') for x in outsider.drain()))
        text = json.dumps({'type':'move','piece':'红车','emoji':'♟😀','esc':'"\\\n\t'}, ensure_ascii=False)
        check('bidirectional BOARD accepted', b.request('BOARD', channel='GENERAL', text=text)[0] == 'OK')
        msg = [x for x in a.drain() if x[0] == 'BOARDMSG']
        check('unicode and escapes preserved; canonical channel', len(msg) == 1 and msg[0][1]['text'] == text and msg[0][1]['channel'] == 'general')
        c.drain()
        check('unhandshaken rejected', server.client().request('BOARD', channel='general', text='x')[1].get('code') == 'not_handshaken')
        for payload, code in [({'channel':'bad channel','text':'x'},'bad_channel'), ({'channel':'general','text':''},'bad_text'), ({'channel':'general','text':17},'bad_text'), ({'channel':'other','text':'x'},'not_in_channel'), ({'channel':'general','text':'棋'*8001},'board_msg_too_large')]:
            check('reject '+code, a.request('BOARD', **payload)[1].get('code') == code)
        check('exact 24000 UTF-8 bytes accepted', a.request('BOARD', channel='general', text='棋'*8000)[0] == 'OK'); b.drain(); c.drain()
        a.request('JOIN', channel='extra')
        check('multiple channels rejected', a.request('BOARD', channel='general', text='x')[1].get('code') == 'single_channel_required')
        a.request('LEAVE', channel='extra'); a.request('LEAVE', channel='general')
        check('zero channels rejected', a.request('BOARD', channel='general', text='x')[1].get('code') == 'single_channel_required')
        a.request('JOIN', channel='general')
        check('unknown command stays an error', a.request('BOARD_INVALID')[1].get('code') == 'unknown_command')
        check('malformed JSON rejected', malformed(a))
        check('connection survives errors', a.request('PING')[0] == 'PONG')
        solo = server.client('rate'); solo.request('LEAVE', channel='general'); solo.request('JOIN', channel='rate')
        replies = [solo.request('BOARD', channel='rate', text='x') for _ in range(180)]
        check('per-connection message rate bounded', any(p.get('code') == 'board_rate_limited' for cmd,p in replies))
        time.sleep(1.05)
        check('rate recovers after window', solo.request('BOARD', channel='rate', text='x')[0] == 'OK')
        ratebytes = server.client('bytes'); ratebytes.request('LEAVE', channel='general'); ratebytes.request('JOIN', channel='bytes')
        replies = [ratebytes.request('BOARD', channel='bytes', text='x'*24000) for _ in range(50)]
        check('per-connection byte rate bounded', any(p.get('code') == 'board_rate_limited' for cmd,p in replies))
    finally: server.close()
    small = Server(dll, ['--max-line','512'])
    try:
        a = small.client('a'*32); b = small.client('b')
        reply = a.request('BOARD', channel='general', text='x'*410)
        check('forwarded line overflow rejected before broadcast', reply[1].get('code') == 'msg_too_large')
        check('rejected overflow is not delivered', not any(x[0] == 'BOARDMSG' for x in b.drain()))
        check('both connections survive outbound overflow', a.request('PING')[0] == b.request('PING')[0] == 'PONG')
        check('small valid message still delivered', a.request('BOARD', channel='general', text='ok')[1].get('delivered') == 1)
        check('small message received intact', any(x[0] == 'BOARDMSG' and x[1]['text'] == 'ok' for x in b.drain()))
        bad = small.client('long')
        bad.s.sendall(b'BOARD '+b'x'*600+b'\n')
        errors = []
        try:
            while True: errors.append(bad.read())
        except (EOFError, ConnectionResetError): pass
        check('inbound oversized line error and disconnect', any(p.get('code') == 'line_too_long' for cmd,p in errors))
    finally: small.close()
    result['passed'] = len(checks)
    if args.out: args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))

def malformed(c):
    c.s.sendall(b'BOARD {not-json}\n')
    cmd, p = c.read()
    return cmd == 'ERR' and p.get('code') == 'bad_request'

if __name__ == '__main__': main()
