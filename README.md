
## Poor Man's T-SQL to PL/pgSQL converter

This is an attempt to make a MSSQL's T-SQL to PostgreSQL's PL/pgSQL converter based on [Poor Man's T-SQL Formatter](https://github.com/TaoK/PoorMansTSqlFormatter).

It's using the unchanged parser and slightly changed formatter, as well as adding an additional step in between that transforms the syntax tree.

It is mostly intended for automating as much work as possible while migrating a project with a large amount of stored procedures and functions,
but should work for any pieces of code containing supported statements.

Still a work in progress

### What should work

 * Creating procedures and functions
 * Select statements in procedures are converted to refcursors
 * Variable declaration and assignment in every possible way
 * Temp tables
 * Table variables. When defined as table, they are converted to temp tables, when using table types - to arrays
 * If statements
 * While loops
 * Unpivot function is implemented but likely requires a bit of manual intervention because there are some hardcoded column names in output
 * Select statements. Top keyword is converted to limit, 'name = value' syntax converted to 'value as name'
 * 'update ... from ...' syntax
 * Some unnecessary things like 'set nocount' and 'lineno' are removed
 * Built-in functions are replaced with native alternatives when possible:
   - len -> length
   - getdate -> now
   - rand -> random
   - newid -> gen_random_uuid
   - isnull -> coalesce
   - format -> to_char
   - datepart -> extract
   - datediff -> extract
   - iif -> case statement
   - cast -> ::
   - try_cast -> try_cast (user defined)
 * Stored procedure calls, including "insert into ... exec ..." statements
 * Transactions are removed because exception handling has implicit rollbacks
 * Try/catch converted to begin/exception, raiserror and throw converted to "raise exception"
 * Since proper type inference is impossible, + for string concatenation and = for booleans are implemented with operator overloading
 * Json functions: json_value, json_query, openjson, isjson, "for json path".
   "For json auto" will likely never really work and is instead handled as "for json path" with a warning comment.
 * Semicolons are added automatically
 * @variableNames are currently renamed to _variable_names,
   #tempTables to _temp_tables, otherNames to other_names.
   This is done to avoid case sensitivity issues. Intended to be used with tools like pgloader that automatically convert table and column names during migration.
 * Data types are converted when possible.

When something is not supported, it will simply be left as is so you can finish the conversion manually.

### Known Issues / Todo

* merge statement haven't been looked at
* some window functions are probably broken
* currently it's just a single .NET 9 console app project, eventually it should be split into a library and a cli

### Usage

dotnet run -i input.sql -o output.sql -D

-D stands for debugging and creates a "debug" directory where tree.txt and tree.psql.txt files are created which contain easily readable syntax trees before and after the trasformation

### Additional mentions

 * [Using PostgreSQL arrays as Dapper parameters](https://medium.com/@zhao.zhongming/how-to-use-composite-object-as-postgresql-stored-procedures-parameter-with-dapper-in-c-8ed1b417f341)
