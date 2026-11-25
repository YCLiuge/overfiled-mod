"""Recover local credentials while preserving every SDK/game identity.
Run without --apply to inspect counts. Applying requires the server to be stopped.
The output directory contains passwords and an offline database backup; keep private.
"""
from pathlib import Path
import argparse
import datetime
import json
import secrets
import socket
import sqlite3
import string


def recover(database, output_dir=None, apply=False):
    database = Path(database).resolve(strict=True)
    live = Path(__file__).resolve().parent / 'Lolo/db/lolo.db'
    if apply and database == live.resolve():
        for port in (18080, 11000, 12000):
            with socket.socket() as sock:
                sock.settimeout(0.3)
                if sock.connect_ex(('127.0.0.1', port)) == 0:
                    raise RuntimeError('Stop the local server gracefully before recovering credentials')
    connection = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)
    connection.row_factory = sqlite3.Row
    users = [dict(row) for row in connection.execute('SELECT user_id,sdk_uid FROM of_user ORDER BY user_id')]
    credentials = [dict(row) for row in connection.execute('SELECT * FROM of_quick ORDER BY id')]
    existing = {row['id']: row for row in credentials}
    missing = [row for row in users if row['sdk_uid'] not in existing]
    result = {'game_accounts': len(users), 'existing_credentials': len(credentials), 'missing_credentials': len(missing)}
    if not apply:
        connection.close()
        return result
    try:
        if output_dir is None:
            raise ValueError('A private output directory is required')
        output = Path(output_dir).resolve()
        output.mkdir(parents=True, exist_ok=False)
    except Exception:
        connection.close()
        raise
    backup = sqlite3.connect(output / 'before-recovery.db')
    connection.backup(backup)
    backup.close()
    connection.close()
    connection = sqlite3.connect(database)
    connection.row_factory = sqlite3.Row
    created = set()
    try:
        connection.execute('BEGIN IMMEDIATE')
        names = {row['username'] for row in credentials}
        for row in missing:
            sdk_id = row['sdk_uid']
            if not isinstance(sdk_id, int) or not 0 < sdk_id <= 0xffffffff:
                raise ValueError('Invalid legacy SDK identity; database remains unchanged')
            name = 'local_' + str(row['user_id'])
            suffix = 0
            while name in names:
                suffix += 1
                name = 'local_' + str(row['user_id']) + '_' + str(suffix)
            names.add(name)
            password = ''.join(secrets.choice(string.ascii_letters + string.digits) for _ in range(16))
            connection.execute('INSERT INTO of_quick (id,username,password,reg_device,user_token,auth_token) VALUES (?,?,?,\'\',\'\',\'\')', (sdk_id, name, password))
            created.add(sdk_id)
        unchanged_users = [dict(row) for row in connection.execute('SELECT user_id,sdk_uid FROM of_user ORDER BY user_id')]
        assert users == unchanged_users, 'Game identity changed'
        for row in credentials:
            assert dict(connection.execute('SELECT * FROM of_quick WHERE id=?', (row['id'],)).fetchone()) == row, 'Existing credential changed'
        accounts = []
        game_ids = {row['sdk_uid']: row['user_id'] for row in users}
        nicknames = {row['user_id']: row['nick_name'] for row in connection.execute('SELECT user_id,nick_name FROM of_game_basic')}
        for row in connection.execute('SELECT id,username,password FROM of_quick ORDER BY id'):
            game_id = game_ids.get(row['id'])
            accounts.append({'username': row['username'], 'password': row['password'], 'sdk_uid': row['id'], 'game_uid': game_id, 'nickname': nicknames.get(game_id), 'source': '本次新建恢复凭据' if row['id'] in created else '原数据库保存密码', 'note': '历史探针身份，通常无需登录' if row['id'] == 2147483647 else ('尚无角色，首次登录创建角色' if game_id is None else '保留原角色')})
        # Persist the private report before committing, so a successful recovery
        # always has usable credentials. No secrets are printed to stdout.
        document = {'generated_at': datetime.datetime.now().astimezone().isoformat(), 'accounts': accounts, 'instructions': ['在游戏SDK账号密码入口输入username和password；游戏UID不能代替登录账号。', '新建恢复密码不是原平台密码，仅适用于本地服务器。', '不要将此文件、JSON文件或数据库备份转发到群聊或加入版本控制。']}
        (output / '账号密码.json').write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding='utf-8')
        lines = ['本地账号登录清单', document['generated_at'], '', *document['instructions'], '']
        for item in accounts:
            lines.extend(['账号：' + item['username'], '密码：' + item['password'], '游戏UID：' + str(item['game_uid'] or '尚无角色'), '昵称：' + str(item['nickname'] or '未设置'), 'SDK UID：' + str(item['sdk_uid']), '来源：' + item['source'], '说明：' + item['note'], ''])
        (output / '账号登录清单.txt').write_text('\n'.join(lines), encoding='utf-8-sig')
        connection.commit()
        result.update(created_credentials=len(created), report_accounts=len(accounts), report=str(output / '账号登录清单.txt'))
    except Exception:
        connection.rollback()
        raise
    finally:
        connection.close()
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', type=Path, default=Path(__file__).resolve().parent / 'Lolo/db/lolo.db')
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    print(json.dumps(recover(args.database, args.output_dir, args.apply), ensure_ascii=True))
