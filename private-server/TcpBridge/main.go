package main

import (
	"context"
	"crypto/aes"
	"crypto/cipher"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"os/signal"
	"strings"
	"sync"
	"time"
)

const maxPayload = 32 * 1024
const handshakeTimeout = 8 * time.Second

var backends = map[string]string{"http": "127.0.0.1:18080", "game": "127.0.0.1:11000", "log": "127.0.0.1:12000"}
var frontends = map[string]string{"http": "127.0.0.1:18081", "game": "127.0.0.1:11001", "log": "127.0.0.1:12001"}

type Config struct {
	Mode   string
	Listen string
	Remote string
	Key    string
}

func loadConfig(path string) (Config, []byte, error) {
	var c Config
	f, err := os.Open(path)
	if err != nil {
		return c, nil, errors.New("cannot open config")
	}
	defer f.Close()
	dec := json.NewDecoder(io.LimitReader(f, 16385))
	dec.DisallowUnknownFields()
	if err = dec.Decode(&c); err != nil {
		return c, nil, errors.New("invalid config JSON")
	}
	var extra any
	if dec.Decode(&extra) != io.EOF {
		return c, nil, errors.New("invalid trailing config data")
	}
	key, err := hex.DecodeString(c.Key)
	if err != nil || len(key) != 32 {
		return c, nil, errors.New("Key must be exactly 64 hexadecimal characters")
	}
	if c.Mode != "server" && c.Mode != "client" {
		return c, nil, errors.New("Mode must be server or client")
	}
	if c.Listen == "" {
		c.Listen = "127.0.0.1:10086"
	}
	if c.Remote == "" {
		c.Remote = "relay.example.com:50505"
	}
	if c.Mode == "server" && c.Listen != "127.0.0.1:10086" {
		return c, nil, errors.New("server Listen must be 127.0.0.1:10086")
	}
	if c.Mode == "client" {
		if _, _, err = net.SplitHostPort(c.Remote); err != nil {
			return c, nil, errors.New("invalid Remote host:port")
		}
	}
	return c, key, nil
}

// Each challenge creates independent directional keys. Prefix + monotonic 64-bit
// counter is never reused within a directional key; counters cannot wrap.
func derive(key, challenge, salt []byte, label string) cipher.AEAD {
	h := hmac.New(sha256.New, key)
	h.Write([]byte("TcpBridge/v1/" + label))
	h.Write(challenge)
	h.Write(salt)
	block, _ := aes.NewCipher(h.Sum(nil))
	a, _ := cipher.NewGCM(block)
	return a
}

type recordIO struct {
	net.Conn
	send, recv       cipher.AEAD
	sp, rp           [4]byte
	sc, rc           uint64
	sw, rw           sync.Mutex
	pending          []byte
	eof, closedWrite bool
}

func nonce(p [4]byte, c uint64) []byte {
	n := make([]byte, 12)
	copy(n, p[:])
	binary.BigEndian.PutUint64(n[4:], c)
	return n
}
func writeAll(w io.Writer, p []byte) error {
	for len(p) > 0 {
		n, e := w.Write(p)
		if e != nil {
			return e
		}
		if n == 0 {
			return io.ErrShortWrite
		}
		p = p[n:]
	}
	return nil
}
func (s *recordIO) sendRecord(p []byte) error {
	if s.sc == ^uint64(0) {
		return errors.New("record counter exhausted")
	}
	var hdr [4]byte
	binary.BigEndian.PutUint32(hdr[:], uint32(len(p)+s.send.Overhead()))
	data := s.send.Seal(nil, nonce(s.sp, s.sc), p, hdr[:])
	s.sc++
	if err := writeAll(s.Conn, hdr[:]); err != nil {
		return err
	}
	return writeAll(s.Conn, data)
}
func (s *recordIO) recvRecord() ([]byte, error) {
	var hdr [4]byte
	if _, err := io.ReadFull(s.Conn, hdr[:]); err != nil {
		if err == io.EOF {
			err = io.ErrUnexpectedEOF
		}
		return nil, err
	}
	n := binary.BigEndian.Uint32(hdr[:])
	if n < 17 || n > maxPayload+17 {
		return nil, errors.New("invalid record length")
	}
	if s.rc == ^uint64(0) {
		return nil, errors.New("record counter exhausted")
	}
	data := make([]byte, int(n))
	if _, err := io.ReadFull(s.Conn, data); err != nil {
		return nil, err
	}
	p, err := s.recv.Open(nil, nonce(s.rp, s.rc), data, hdr[:])
	s.rc++
	if err != nil {
		return nil, errors.New("record authentication failed")
	}
	return p, nil
}
func (s *recordIO) Write(p []byte) (int, error) {
	s.sw.Lock()
	defer s.sw.Unlock()
	if s.closedWrite {
		return 0, net.ErrClosed
	}
	total := 0
	for len(p) > 0 {
		n := len(p)
		if n > maxPayload {
			n = maxPayload
		}
		b := make([]byte, n+1)
		b[0] = 1
		copy(b[1:], p[:n])
		if e := s.sendRecord(b); e != nil {
			return total, e
		}
		total += n
		p = p[n:]
	}
	return total, nil
}
func (s *recordIO) Read(p []byte) (int, error) {
	s.rw.Lock()
	defer s.rw.Unlock()
	if len(p) == 0 {
		return 0, nil
	}
	if len(s.pending) == 0 {
		if s.eof {
			return 0, io.EOF
		}
		b, e := s.recvRecord()
		if e != nil {
			return 0, e
		}
		if len(b) == 1 && b[0] == 2 {
			s.eof = true
			return 0, io.EOF
		}
		if len(b) < 2 || b[0] != 1 {
			return 0, errors.New("invalid data record")
		}
		s.pending = b[1:]
	}
	n := copy(p, s.pending)
	s.pending = s.pending[n:]
	return n, nil
}
func (s *recordIO) CloseWrite() error {
	s.sw.Lock()
	defer s.sw.Unlock()
	if s.closedWrite {
		return nil
	}
	s.closedWrite = true
	e := s.sendRecord([]byte{2})
	if c, ok := s.Conn.(interface{ CloseWrite() error }); ok {
		if ce := c.CloseWrite(); e == nil {
			e = ce
		}
	}
	return e
}

func clientHandshake(c net.Conn, key []byte, route string, probe bool) (*recordIO, error) {
	c.SetDeadline(time.Now().Add(handshakeTimeout))
	hello := make([]byte, 44)
	if _, e := io.ReadFull(c, hello); e != nil {
		return nil, e
	}
	if string(hello[:8]) != "OFTCP001" {
		return nil, errors.New("invalid server protocol")
	}
	client := make([]byte, 36)
	if _, e := rand.Read(client); e != nil {
		return nil, e
	}
	if e := writeAll(c, client); e != nil {
		return nil, e
	}
	s := &recordIO{Conn: c, send: derive(key, hello[8:40], client[:32], "c2s"), recv: derive(key, hello[8:40], client[:32], "s2c")}
	copy(s.sp[:], client[32:])
	copy(s.rp[:], hello[40:])
	mode := byte(0)
	if probe {
		mode = 1
	}
	req := append([]byte{3, mode}, []byte(route)...)
	if e := s.sendRecord(req); e != nil {
		return nil, e
	}
	resp, e := s.recvRecord()
	if e != nil {
		return nil, e
	}
	if len(resp) != 2 || resp[0] != 4 || resp[1] != 0 {
		return nil, errors.New("backend unavailable or route rejected")
	}
	c.SetDeadline(time.Time{})
	return s, nil
}

type dialFunc func(context.Context, string, string) (net.Conn, error)

func defaultDial(ctx context.Context, network, address string) (net.Conn, error) {
	return (&net.Dialer{Timeout: 5 * time.Second, KeepAlive: 30 * time.Second}).DialContext(ctx, network, address)
}
func serverConnection(ctx context.Context, c net.Conn, key []byte, dial dialFunc) error {
	defer c.Close()
	c.SetDeadline(time.Now().Add(handshakeTimeout))
	hello := make([]byte, 44)
	copy(hello, "OFTCP001")
	if _, e := rand.Read(hello[8:]); e != nil {
		return e
	}
	if e := writeAll(c, hello); e != nil {
		return e
	}
	client := make([]byte, 36)
	if _, e := io.ReadFull(c, client); e != nil {
		return e
	}
	s := &recordIO{Conn: c, send: derive(key, hello[8:40], client[:32], "s2c"), recv: derive(key, hello[8:40], client[:32], "c2s")}
	copy(s.sp[:], hello[40:])
	copy(s.rp[:], client[32:])
	req, e := s.recvRecord()
	if e != nil {
		return errors.New("client authentication failed")
	}
	if len(req) < 3 || len(req) > 6 || req[0] != 3 || req[1] > 1 {
		return errors.New("invalid authenticated request")
	}
	addr, ok := backends[string(req[2:])]
	if !ok {
		return errors.New("unknown route")
	}
	backend, e := dial(ctx, "tcp", addr)
	if e != nil {
		_ = s.sendRecord([]byte{4, 1})
		return errors.New("backend unavailable")
	}
	defer backend.Close()
	if e = s.sendRecord([]byte{4, 0}); e != nil {
		return e
	}
	if req[1] == 1 {
		return nil
	}
	c.SetDeadline(time.Time{})
	bridge(s, backend)
	return nil
}
func halfClose(c net.Conn) {
	if h, ok := c.(interface{ CloseWrite() error }); ok {
		_ = h.CloseWrite()
	} else {
		_ = c.Close()
	}
}
func bridge(a, b net.Conn) {
	defer a.Close()
	defer b.Close()
	done := make(chan struct{}, 2)
	copyOne := func(dst, src net.Conn) {
		_, e := io.Copy(dst, src)
		if e != nil {
			a.Close()
			b.Close()
		} else {
			halfClose(dst)
			deadline := time.Now().Add(30 * time.Second)
			a.SetDeadline(deadline)
			b.SetDeadline(deadline)
		}
		done <- struct{}{}
	}
	go copyOne(a, b)
	go copyOne(b, a)
	<-done
	<-done
}
func serve(ctx context.Context, l net.Listener, handler func(net.Conn)) error {
	var mu sync.Mutex
	active := map[net.Conn]bool{}
	var wg sync.WaitGroup
	finished := make(chan struct{})
	go func() {
		select {
		case <-ctx.Done():
			l.Close()
			mu.Lock()
			for c := range active {
				c.Close()
			}
			mu.Unlock()
		case <-finished:
		}
	}()
	defer func() {
		close(finished)
		l.Close()
		mu.Lock()
		for c := range active {
			c.Close()
		}
		mu.Unlock()
		wg.Wait()
	}()
	for {
		c, e := l.Accept()
		if e != nil {
			if ctx.Err() != nil {
				return nil
			}
			return e
		}
		mu.Lock()
		if ctx.Err() != nil {
			mu.Unlock()
			c.Close()
			return nil
		}
		active[c] = true
		wg.Add(1)
		mu.Unlock()
		go func() {
			defer wg.Done()
			defer c.Close()
			defer func() { mu.Lock(); delete(active, c); mu.Unlock() }()
			handler(c)
		}()
	}
}
func probeRemote(ctx context.Context, remote string, key []byte) error {
	for _, route := range []string{"http", "game", "log"} {
		c, e := defaultDial(ctx, "tcp", remote)
		if e != nil {
			return fmt.Errorf("%s: remote connection failed", route)
		}
		_, e = clientHandshake(c, key, route, true)
		c.Close()
		if e != nil {
			return fmt.Errorf("%s: authentication or backend check failed", route)
		}
		log.Printf("probe %s: OK", route)
	}
	return nil
}
func run(ctx context.Context, c Config, key []byte, probe bool) error {
	if probe {
		return probeRemote(ctx, c.Remote, key)
	}
	if c.Mode == "server" {
		l, e := net.Listen("tcp", c.Listen)
		if e != nil {
			return errors.New("server listen failed")
		}
		log.Printf("server listening %s", c.Listen)
		return serve(ctx, l, func(conn net.Conn) {
			if e := serverConnection(ctx, conn, key, defaultDial); e != nil {
				log.Print("connection rejected or closed: ", e)
			}
		})
	}
	listeners := map[string]net.Listener{}
	defer func() {
		for _, l := range listeners {
			l.Close()
		}
	}()
	for route, addr := range frontends {
		l, e := net.Listen("tcp", addr)
		if e != nil {
			return fmt.Errorf("%s listener unavailable", route)
		}
		listeners[route] = l
	}
	child, cancel := context.WithCancel(ctx)
	defer cancel()
	errs := make(chan error, 3)
	for route, l := range listeners {
		route, l := route, l
		log.Printf("client %s listening %s", route, l.Addr())
		go func() {
			errs <- serve(child, l, func(local net.Conn) {
				remote, e := defaultDial(child, "tcp", c.Remote)
				if e != nil {
					log.Print("remote connection failed")
					return
				}
				defer remote.Close()
				secure, e := clientHandshake(remote, key, route, false)
				if e != nil {
					log.Print("remote authentication or backend check failed")
					return
				}
				bridge(local, secure)
			})
		}()
	}
	e := <-errs
	cancel()
	<-errs
	<-errs
	return e
}
func main() {
	path := flag.String("config", "config.json", "JSON configuration path")
	probe := flag.Bool("probe", false, "authenticate remote and check all three backends, without forwarding")
	flag.Parse()
	c, key, e := loadConfig(*path)
	if e != nil {
		log.Print(e)
		os.Exit(1)
	}
	if *probe && strings.TrimSpace(c.Remote) == "" {
		log.Print("Remote required for probe")
		os.Exit(1)
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	if e = run(ctx, c, key, *probe); e != nil {
		log.Print(e)
		os.Exit(1)
	}
}
