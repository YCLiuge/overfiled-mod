package db

import (
	"errors"
	"path/filepath"
	"reflect"
	"testing"
	"time"

	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
	"gorm.io/gorm/schema"

	"gucooing/lolo/pkg/cache"
)

// Keep these tests sequential: the package DB and cache are shared globals.
func nicknameTestDB(t *testing.T) *gorm.DB {
	t.Helper()
	local, err := gorm.Open(sqlite.Open(filepath.Join(t.TempDir(), "nickname.sqlite")), &gorm.Config{
		NamingStrategy: schema.NamingStrategy{SingularTable: true},
		Logger:         logger.Default.LogMode(logger.Silent),
	})
	if err != nil {
		t.Fatal(err)
	}
	pool, err := local.DB()
	if err != nil {
		t.Fatal(err)
	}
	pool.SetMaxOpenConns(1)
	pool.SetMaxIdleConns(1)
	originalDB, originalCache := db, basicCache
	t.Cleanup(func() {
		db, basicCache = originalDB, originalCache
		_ = pool.Close()
	})
	if err := local.AutoMigrate(&OFGameBasic{}); err != nil {
		t.Fatal(err)
	}
	db = local
	basicCache = cache.New[uint32, *OFGameBasic](0)
	return local
}

func readNicknameBasic(t *testing.T, local *gorm.DB, uid uint32) *OFGameBasic {
	t.Helper()
	row := &OFGameBasic{UserId: uid}
	if err := local.First(row).Error; err != nil {
		t.Fatal(err)
	}
	return row
}

func TestGameBasicNicknameNewPlayer(t *testing.T) {
	local := nicknameTestDB(t)
	const uid uint32 = 4294967295
	got, err := GetGameBasicOrCreate(uid)
	if err != nil {
		t.Fatal(err)
	}
	if got.NickName != "旅行者4294967295" || got.UserId != uid {
		t.Fatalf("wrong new player: %+v", got)
	}
	persisted := readNicknameBasic(t, local, uid)
	// SQLite preserves timestamp instants, but not time.Location identity.
	returned := *got
	if !returned.CreatedAt.Equal(persisted.CreatedAt) || !returned.UpdatedAt.Equal(persisted.UpdatedAt) {
		t.Fatal("new player timestamps did not persist")
	}
	returned.CreatedAt, returned.UpdatedAt = persisted.CreatedAt, persisted.UpdatedAt
	if !reflect.DeepEqual(&returned, persisted) {
		t.Fatalf("returned data differs from persisted data: %+v / %+v", got, persisted)
	}
	if got.Level != 30 || got.Head != 41101 || got.CreatedAt.IsZero() {
		t.Fatalf("new player defaults lost: %+v", got)
	}
	cached, ok := basicCache.Get(uid)
	if !ok || cached != got {
		t.Fatal("new nickname not cached")
	}
}

func TestGameBasicNicknameExistingPlayers(t *testing.T) {
	for _, name := range []string{"", " 原有♥昵称 ", " "} {
		for _, cached := range []bool{false, true} {
			t.Run(name+map[bool]string{false: "/uncached", true: "/cached"}[cached], func(t *testing.T) {
				local := nicknameTestDB(t)
				seed := &OFGameBasic{
					UserId: 1000006, NickName: name, Level: 17, Exp: 123,
					Birthday: "2000-02-03", Sign: "keep this", LastLoginTime: 12345,
					Head: 41102, CharacterId: 101002, IsHideBirthday: true,
					CreatedAt: time.Date(2020, 2, 3, 4, 5, 6, 0, time.UTC),
					UpdatedAt: time.Date(2021, 3, 4, 5, 6, 7, 0, time.UTC),
				}
				if err := local.Create(seed).Error; err != nil {
					t.Fatal(err)
				}
				before := readNicknameBasic(t, local, seed.UserId)
				if cached {
					entry := *before
					if name == "" {
						entry.Birthday = "stale cached birthday"
						entry.Exp = 999999
					}
					basicCache.Set(seed.UserId, &entry)
				}
				got, err := GetGameBasicOrCreate(seed.UserId)
				if err != nil {
					t.Fatal(err)
				}
				expected := *before
				if expected.NickName == "" {
					expected.NickName = "旅行者1000006"
				}
				persisted := readNicknameBasic(t, local, seed.UserId)
				if !reflect.DeepEqual(got, &expected) || !reflect.DeepEqual(persisted, &expected) {
					t.Fatalf("only nickname may change: got=%+v persisted=%+v expected=%+v", got, persisted, expected)
				}
				shared, ok := GetGameBasic(seed.UserId)
				if !ok || shared.NickName != expected.NickName {
					t.Fatal("shared getter still returns an empty cached nickname")
				}
			})
		}
	}
}

func TestGameBasicNicknameNullIsRepaired(t *testing.T) {
	local := nicknameTestDB(t)
	seed := &OFGameBasic{UserId: 43, Birthday: "2000-01-01"}
	if err := local.Create(seed).Error; err != nil {
		t.Fatal(err)
	}
	if err := local.Model(seed).UpdateColumn("nick_name", nil).Error; err != nil {
		t.Fatal(err)
	}
	before := readNicknameBasic(t, local, seed.UserId)
	got, err := GetGameBasicOrCreate(seed.UserId)
	if err != nil {
		t.Fatal(err)
	}
	expected := *before
	expected.NickName = "旅行者43"
	if !reflect.DeepEqual(got, &expected) || !reflect.DeepEqual(readNicknameBasic(t, local, seed.UserId), &expected) {
		t.Fatalf("NULL nickname not repaired without changing other fields: %+v", got)
	}
}

func TestGameBasicNicknameConcurrentRenameWins(t *testing.T) {
	for _, cached := range []bool{false, true} {
		t.Run(map[bool]string{false: "uncached", true: "cached"}[cached], func(t *testing.T) {
			local := nicknameTestDB(t)
			seed := &OFGameBasic{UserId: 42, NickName: "", Birthday: "2000-01-01"}
			if err := local.Create(seed).Error; err != nil {
				t.Fatal(err)
			}
			if cached {
				basicCache.Set(seed.UserId, seed)
			}
			// Deterministically interleave a real rename after reading the empty
			// name but before the repair UPDATE begins its transaction.
			renamed := false
			if err := local.Callback().Update().Before("gorm:begin_transaction").Register("test:rename", func(tx *gorm.DB) {
				if renamed {
					return
				}
				renamed = true
				if err := local.Model(&OFGameBasic{}).Where("user_id = ?", seed.UserId).
					UpdateColumn("nick_name", "玩家刚设置的名字").Error; err != nil {
					tx.AddError(err)
				}
			}); err != nil {
				t.Fatal(err)
			}
			got, err := GetGameBasicOrCreate(seed.UserId)
			if err != nil || got == nil || got.NickName != "玩家刚设置的名字" {
				t.Fatalf("concurrent rename was lost: %+v %v", got, err)
			}
			if persisted := readNicknameBasic(t, local, seed.UserId); persisted.NickName != got.NickName || persisted.Birthday != seed.Birthday {
				t.Fatalf("incorrect persistence: %+v", persisted)
			}
		})
	}
}

func TestGameBasicNicknameDBFailures(t *testing.T) {
	for _, stage := range []string{"initial read", "create", "update", "cached update", "reread"} {
		t.Run(stage, func(t *testing.T) {
			local := nicknameTestDB(t)
			const uid uint32 = 42
			seed := &OFGameBasic{UserId: uid}
			if stage != "create" && stage != "initial read" {
				if err := local.Create(seed).Error; err != nil {
					t.Fatal(err)
				}
			}
			if stage == "cached update" || stage == "reread" {
				basicCache.Set(uid, seed)
			}
			failure := errors.New("injected nickname DB failure")
			fail := func(tx *gorm.DB) { tx.AddError(failure) }
			var err error
			switch stage {
			case "initial read", "reread":
				err = local.Callback().Query().Before("gorm:query").Register("test:fail", fail)
			case "create":
				err = local.Callback().Create().Before("gorm:create").Register("test:fail", fail)
			default:
				err = local.Callback().Update().Before("gorm:update").Register("test:fail", fail)
			}
			if err != nil {
				t.Fatal(err)
			}
			got, err := GetGameBasicOrCreate(uid)
			if got != nil || !errors.Is(err, failure) {
				t.Fatalf("DB failure reported as success: %+v %v", got, err)
			}
			if cached, ok := basicCache.Get(uid); ok && cached.NickName != "" {
				t.Fatal("failure published a fabricated nickname in cache")
			}
			if seed.NickName != "" {
				t.Fatal("failed repair mutated the shared cached pointer")
			}
		})
	}
}
