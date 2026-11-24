package db

import (
	"context"
	"errors"
	"path/filepath"
	"testing"
	"time"

	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
)

func TestPrivateChatSingleConnectionTransactions(t *testing.T) {
	local, err := gorm.Open(sqlite.Open(filepath.Join(t.TempDir(), "chat.sqlite")), &gorm.Config{})
	if err != nil {
		t.Fatal(err)
	}
	pool, err := local.DB()
	if err != nil {
		t.Fatal(err)
	}
	pool.SetMaxOpenConns(1)
	pool.SetMaxIdleConns(1)
	t.Cleanup(func() { _ = pool.Close() })
	if err := local.AutoMigrate(&OFChatPrivate{}, &OFChatPrivateMsg{}); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	original := db
	db = local.WithContext(ctx)
	defer func() { db = original }()

	// Reading a new conversation must reuse the transaction's only connection.
	empty, err := GetAllChatPrivateMsg(21, 11)
	if err != nil || len(empty) != 0 {
		t.Fatalf("read new conversation: %v, %v", empty, err)
	}
	msg := &OFChatPrivateMsg{UserId: 11, OFChatMsg: &OFChatMsg{SendTime: 123, Text: "saved"}}
	if err := CreateChatPrivateMsg(21, msg); err != nil {
		t.Fatalf("create with one connection: %v", err)
	}
	private, err := GetChatPrivate(11, 21)
	if err != nil || !private.IsNewMsg {
		t.Fatalf("unread conversation: %+v %v", private, err)
	}
	records, err := GetAllChatPrivateMsg(21, 11)
	if err != nil || len(records) != 1 || records[0].Text != "saved" || records[0].PrivateID != private.ID {
		t.Fatalf("read saved conversation: %+v %v", records, err)
	}
	private, err = GetChatPrivate(21, 11)
	if err != nil || private.IsNewMsg {
		t.Fatalf("read must clear unread: %+v %v", private, err)
	}
	other, err := GetChatPrivate(31, 11)
	if err != nil || other.ID == private.ID {
		t.Fatalf("different user pairs shared conversation: %+v %v", other, err)
	}

	// An insert failure must roll back the unread flag along with the message.
	failure := errors.New("injected chat insert failure")
	if err := db.Callback().Create().Before("gorm:create").Register("test:chat-failure", func(tx *gorm.DB) {
		if tx.Statement.Table == "of_chat_private_msgs" {
			tx.AddError(failure)
		}
	}); err != nil {
		t.Fatal(err)
	}
	err = CreateChatPrivateMsg(21, &OFChatPrivateMsg{UserId: 11, OFChatMsg: &OFChatMsg{Text: "rolled back"}})
	if !errors.Is(err, failure) {
		t.Fatalf("expected insert failure: %v", err)
	}
	private, err = GetChatPrivate(11, 21)
	if err != nil || private.IsNewMsg {
		t.Fatalf("failed insert left unread flag: %+v %v", private, err)
	}
	var count int64
	if err := db.Model(&OFChatPrivateMsg{}).Count(&count).Error; err != nil || count != 1 {
		t.Fatalf("message rollback count=%d err=%v", count, err)
	}
	if stats := pool.Stats(); stats.WaitCount != 0 || stats.InUse != 0 {
		t.Fatalf("connection leak/reentry: %+v", stats)
	}
}
