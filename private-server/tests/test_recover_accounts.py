from contextlib import closing
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('recovery', Path(__file__).resolve().parents[1] / 'Recover-LocalAccounts.py')
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.db = self.root / 'fixture.db'
        with closing(sqlite3.connect(self.db)) as c, c:
            c.executescript('''
            CREATE TABLE of_user(user_id INTEGER PRIMARY KEY,sdk_uid INTEGER UNIQUE);
            CREATE TABLE of_quick(id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE, password TEXT NOT NULL, reg_device TEXT NOT NULL, user_token TEXT, auth_token TEXT NOT NULL);
            CREATE TABLE of_game_basic(user_id INTEGER PRIMARY KEY,nick_name TEXT);
            INSERT INTO of_user VALUES(1000000,1),(1000001,7),(1000002,2147483647);
            INSERT INTO of_game_basic VALUES(1000000,'已有角色'),(1000001,'旧角色');
            INSERT INTO of_quick VALUES(1,'original','original-password','device','user-token','auth-token');
            INSERT INTO of_quick VALUES(9,'local_1000001','collision-password','','','');
            ''')

    def test_dry_run_preserves_database(self):
        before = self.db.read_bytes()
        stats = recovery.recover(self.db)
        self.assertEqual(stats['missing_credentials'], 2)
        self.assertEqual(before, self.db.read_bytes())
        self.assertEqual(sorted(p.name for p in self.root.iterdir()), ['fixture.db'])

    def test_preserves_passwords_roles_collision_and_repeated_recovery(self):
        with closing(sqlite3.connect(self.db)) as c, c:
            originals = c.execute('select * from of_quick order by id').fetchall()
            roles = c.execute('select * from of_user order by user_id').fetchall()
        report = self.root / 'private'
        stats = recovery.recover(self.db, report, True)
        self.assertEqual(stats['created_credentials'], 2)
        data = json.loads((report / '账号密码.json').read_text(encoding='utf-8'))
        self.assertEqual(len(data['accounts']), 4)
        accounts = {a['sdk_uid']: a for a in data['accounts']}
        self.assertEqual(accounts[1]['password'], 'original-password')
        self.assertEqual(accounts[7]['username'], 'local_1000001_1')
        self.assertEqual(accounts[7]['game_uid'], 1000001)
        self.assertEqual(accounts[7]['nickname'], '旧角色')
        self.assertEqual(len(accounts[7]['password']), 16)
        self.assertTrue(accounts[7]['password'].isalnum())
        self.assertNotIn('auth-token', json.dumps(data))
        with closing(sqlite3.connect(self.db)) as c, c:
            self.assertEqual(c.execute('select * from of_user order by user_id').fetchall(), roles)
            for row in originals:
                self.assertEqual(c.execute('select * from of_quick where id=?', (row[0],)).fetchone(), row)
            first = c.execute('select * from of_quick order by id').fetchall()
        with closing(sqlite3.connect(report / 'before-recovery.db')) as c, c:
            self.assertEqual(c.execute('select * from of_quick order by id').fetchall(), originals)
        self.assertEqual(recovery.recover(self.db, self.root / 'again', True)['created_credentials'], 0)
        with closing(sqlite3.connect(self.db)) as c, c:
            self.assertEqual(c.execute('select * from of_quick order by id').fetchall(), first)
        with self.assertRaises(FileExistsError):
            recovery.recover(self.db, report, True)

    def test_invalid_identity_rolls_back(self):
        with closing(sqlite3.connect(self.db)) as c, c:
            c.execute('insert into of_user values(1000003,0)')
        with self.assertRaises(ValueError):
            recovery.recover(self.db, self.root / 'invalid', True)
        with closing(sqlite3.connect(self.db)) as c, c:
            self.assertEqual(c.execute('select count(*) from of_quick').fetchone()[0], 2)


if __name__ == '__main__':
    unittest.main()
