# SQL Console Log

Whether a request fired 2 SQL statements or 143 is not something you can see statement by statement, because each one of them is fast. Turn on `SmartAdmin:Database:SqlLog` and every statement gets a block, with a summary line added once the request ends, and that summary is where the difference lands. It ships off; this is a development-time tool.

The slow-SQL threshold `Database:SlowSqlMillis` and the failed-SQL log are a different pair. Those are on by default and should stay on in production; they're covered in the [deployment guide](/guide/deployment/).

## Switches

```json
{
  "SmartAdmin": {
    "Database": {
      "SqlLog": {
        "Enabled": true,
        "MinMillis": 0,
        "MaxSqlChars": 4000,
        "RequestSummary": true,
        "Color": null
      }
    }
  }
}
```

| Key | Default | What it does |
| --- | --- | --- |
| `Enabled` | `false` | Master switch |
| `MinMillis` | `0` | Only print statements taking at least this long (ms); `0` prints all |
| `MaxSqlChars` | `4000` | Print limit for one statement; longer ones are truncated with a note. A batch insert can run to hundreds of thousands of characters |
| `RequestSummary` | `true` | Whether to add a summary line when the request ends |
| `Color` | auto | ANSI color. Left unset, colors appear only when stdout is a terminal and `NO_COLOR` is not set |

If file logging is on at the same time, set `Color` to `false` explicitly, or escape sequences end up mixed into the files.

`MinMillis` gates the statement blocks only, not the statistics: statements it filters out still count toward the summary's total count and total elapsed time. No single query in an N+1 is slow, so if filtering by duration also shrank the count, the switch would have defeated its own purpose.

## One block per statement

```text
info: SmartAdmin.Sql[0]
      ┌ SQL #1 查询 [SmartAdmin] 3.4 ms
      │ SELECT COUNT(1) FROM "sys_user" WHERE ( "IsDelete" = 0 ) AND ( "OrgId" IN (100,101) )
      └ 参数 2 个(已内联)
info: SmartAdmin.Sql[0]
      ┌ SQL #2 查询 [SmartAdmin] 11.8 ms
      │ SELECT "Id","Account","RealName","OrgId" FROM "sys_user" WHERE ( "IsDelete" = 0 ) AND ( "OrgId" IN (100,101) ) ORDER BY "Id" DESC LIMIT 10 OFFSET 0
      └ 参数 2 个(已内联)
```

The block header carries, in order: sequence number, statement kind, `ConfigId`, elapsed time. The sequence number matches the "slowest was #n" in the summary line. Kind is decided by the leading keyword, and a CTE query starting with `WITH` still counts as a query. `ConfigId` is fixed at `SmartAdmin` for the main database and is whatever name you gave the connection under `AdditionalDatabases` otherwise, so you can see at a glance which database a statement hit.

Color lands in two places. Kind is colored by category: query green, insert yellow, update blue, delete red, DDL magenta, other gray. Elapsed time is bucketed against the slow-SQL threshold: over the line red, past halfway yellow, everything else green.

Parameters are not listed on a separate line; they're inlined into the statement. What you want while debugging is one SQL statement you can paste into a client and run, not "statement here, parameters there" to be reassembled by hand. Booleans render uniformly as `1` or `0`, which runs as-is on SQLite, MySQL and SqlServer; on PostgreSQL you have to adjust it yourself.

## The summary line is the way into N+1

```text
info: SmartAdmin.Sql[0]
      └ SQL 汇总 GET /api/v1/sys/user/page → 2 条 / 15.2 ms / 最慢 #2 11.8 ms
```

Count, total elapsed, and which one was slowest. This is the line that makes good on the opening claim. At 20 statements it also calls the problem out at the end of the line:

```text
info: SmartAdmin.Sql[0]
      └ SQL 汇总 GET /api/v1/sys/order/page → 143 条 / 812.6 ms / 最慢 #7 24.1 ms  ← 143 条,疑似 N+1
```

Only HTTP requests get a summary. A request that ran no statements at all (static assets, 404s) doesn't take up the line; a job trigger goes down the same per-statement path and still prints blocks, but gets no summary line.

## Sensitive parameters print as `'***'`

Any parameter whose name matches `password`, `pwd`, `secret`, `token`, `credential`, `header`, `authorization`, `apikey`, `api_key` or `cookie` has its value rendered as `'***'`. Matching is by substring, so `newPassword` and `access_token` count too.

```text
      ┌ SQL #4 修改 [SmartAdmin] 2.1 ms
      │ UPDATE "sys_user" SET "Password"='***' , "UpdateTime"='2026-09-06 10:21:33.417' WHERE "Id"=1
      └ 参数 3 个(已内联)
```

The kernel deliberately does not build this line with SqlSugar's own `UtilMethods.GetSqlString`, because that would dump password hashes and TOTP seeds verbatim. This log gets screenshotted into support tickets.

The list (`SensitiveKeys`) is shared with the operation log's parameter redaction, and the failed-SQL and slow-SQL diagnostics mask against the same one. Two outlets keeping two lists means patching one and missing the other, and the sensitive value still reaches disk down the other path.

::: warning Masking only reads names
The test is whether the parameter's name looks like a credential, not what its value holds. Sensitive columns in a consumer's own tables whose names carry none of those words (national ID, bank card, medical findings) are inlined into the statement in the clear.
:::

## The channel is `ILogger("SmartAdmin.Sql")`

Statement blocks and summary lines both leave through this log category rather than going straight to the console. As a result:

- File logging (`Logging:File`) picks them up as well, so you don't have to sit watching a terminal.
- The level is tunable on its own. Set `Logging:LogLevel:SmartAdmin.Sql` to `Warning` and ordinary statement blocks (`Information`) go quiet while the ones over the slow-SQL threshold (`Warning`) stay.
- A structured sink receives the template plus its arguments, with sequence number, kind, `ConfigId` and elapsed time each as its own field.

## Don't turn it on in production

Log volume is the first layer: several statements per request, at least three lines each, every one carrying the full statement. The second layer costs more. The masking above only reads parameter names, so values whose names miss the list land verbatim in your log files, your log pipeline, and the hands of anyone with permission to read either.

Raising `MinMillis` is not the same as turning it off; it just prints fewer statement blocks while the statistics and the summary keep running. To turn it off, set `Enabled` back to `false`.

To keep a performance trail in production, `Database:SlowSqlMillis` is enough: it prints only the statements that cross the line, which is orders of magnitude less.
