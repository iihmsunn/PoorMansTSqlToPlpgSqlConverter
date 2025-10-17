CREATE OR REPLACE FUNCTION try_cast(_in text, INOUT _out ANYELEMENT)
  LANGUAGE plpgsql immutable AS
$func$
BEGIN
   EXECUTE format('SELECT %L::%s', $1, pg_typeof(_out))
   INTO  _out;
EXCEPTION WHEN others THEN
   -- do nothing: _out already carries default
END
$func$;
