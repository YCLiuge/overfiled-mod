package db

import (
	"path/filepath"
	"sync"
	"testing"
)

func TestSDKAccountAllocationAndCredentials(t *testing.T) {
	if err := NewDB(&Option{Type: Sqlite, Dsn: filepath.Join(t.TempDir(), "accounts.sqlite"), MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := Close(); err != nil {
			t.Error(err)
		}
	})
	for i, uid := range []uint32{1, 3, 2147483647} {
		if _, err := CreateOFUser(&OFUser{UserId: uint32(1100000 + i), SdkUid: uid}); err != nil {
			t.Fatal(err)
		}
	}
	legacy := &OFQuick{ID: 1175555, Username: "legacy-synthetic", Password: "unchanged"}
	if err := db.Create(legacy).Error; err != nil {
		t.Fatal(err)
	}
	q, err := OrCreateOFQuick("quote\"雪\\account", "quote\"\\password 雪")
	if err != nil {
		t.Fatal(err)
	}
	if q.ID != 2 {
		t.Fatalf("expected first free SDK ID 2; got %d", q.ID)
	}
	again, err := OrCreateOFQuick(q.Username, "attempted-replacement")
	if err != nil {
		t.Fatal(err)
	}
	if again.ID != q.ID || again.Password != q.Password {
		t.Fatal("existing account password/ID replaced")
	}
	if err := UpdateOFQuickTokens(q.ID, "auth☃️123", "session☃️123"); err != nil {
		t.Fatal(err)
	}
	q2, err := GetOFQuick(q.ID)
	if err != nil {
		t.Fatal(err)
	}
	if q2.Password != q.Password || q2.Username != q.Username {
		t.Fatal("token refresh overwrote account identity")
	}
	for _, pair := range [][2]string{{"", "x"}, {" ", "x"}, {"x", ""}, {"x", " "}} {
		if _, err := OrCreateOFQuick(pair[0], pair[1]); err == nil {
			t.Fatal("empty credentials accepted")
		}
	}
	var wg sync.WaitGroup
	ids := make(chan uint32, 8)
	errs := make(chan error, 8)
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			q, err := OrCreateOFQuick("same-first-login", "first-password")
			if err != nil {
				errs <- err
				return
			}
			ids <- q.ID
		}()
	}
	wg.Wait()
	close(ids)
	close(errs)
	for err := range errs {
		t.Error(err)
	}
	var id uint32
	for got := range ids {
		if id != 0 && id != got {
			t.Fatal("concurrent first login split account")
		}
		id = got
	}
	if id == 0 || id == 1 || id == 3 || id >= 2147483647 {
		t.Fatalf("bad allocated ID %d", id)
	}
	original, err := GetOFQuick(1175555)
	if err != nil || original.Password != "unchanged" {
		t.Fatal("explicit legacy account changed")
	}
}
