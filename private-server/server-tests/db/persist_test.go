package db

import (
	"bytes"
	"path/filepath"
	"sync"
	"testing"
	"time"
)

// A file-backed temporary SQLite DB exercises the real gzip and SQL paths.
func TestLocalPersistDrainsSQLiteAndPreservesOrder(t *testing.T) {
	if err := NewDB(&Option{Type: Sqlite, Dsn: filepath.Join(t.TempDir(), "drain.sqlite"), MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := Close(); err != nil {
			t.Error(err)
		}
	})
	entered, release := make(chan struct{}), make(chan struct{})
	Persist(7, func() error { close(entered); <-release; return SaveOFGameBin(7, []byte(`{"version":1}`)) })
	<-entered
	Persist(7, func() error { return SaveOFGameBin(7, []byte(`{"version":2}`)) })
	stopped := make(chan struct{})
	go func() { StopPersist(); close(stopped) }()
	select {
	case <-stopped:
		t.Fatal("StopPersist returned before in-flight job finished")
	case <-time.After(20 * time.Millisecond):
	}
	close(release)
	select {
	case <-stopped:
	case <-time.After(5 * time.Second):
		t.Fatal("worker drain stuck")
	}
	row, err := GetOFGameByUserId(7)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(row.BinData, []byte(`{"version":2}`)) {
		t.Fatalf("queued writes out of order: %s", row.BinData)
	}
	// Once stopped, fallback must still write, after all queued writes.
	Persist(7, func() error { return SaveOFGameBin(7, []byte(`{"version":3}`)) })
	row, err = GetOFGameByUserId(7)
	if err != nil || !bytes.Equal(row.BinData, []byte(`{"version":3}`)) {
		t.Fatalf("fallback roundtrip: %s %v", row.BinData, err)
	}
	StopPersist() // repeated stops are safe
}

func TestLocalPersistConcurrentStopAndSubmit(t *testing.T) {
	StartPersist(2, 2)
	var mu sync.Mutex
	count := 0
	var workers sync.WaitGroup
	for i := 0; i < 8; i++ {
		workers.Add(1)
		go func(key uint32) {
			defer workers.Done()
			for j := 0; j < 50; j++ {
				Persist(key, func() error { mu.Lock(); count++; mu.Unlock(); return nil })
			}
		}(uint32(i))
	}
	var stoppers sync.WaitGroup
	for i := 0; i < 3; i++ {
		stoppers.Add(1)
		go func() { defer stoppers.Done(); StopPersist() }()
	}
	workers.Wait()
	stoppers.Wait()
	StopPersist()
	mu.Lock()
	defer mu.Unlock()
	if count != 400 {
		t.Fatalf("lost jobs: got %d want 400", count)
	}
}
