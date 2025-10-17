CREATE OR REPLACE FUNCTION dbo.concat_ws_text(text, VARIADIC text[])
  RETURNS text
  LANGUAGE internal IMMUTABLE PARALLEL SAFE AS
'text_concat_ws';