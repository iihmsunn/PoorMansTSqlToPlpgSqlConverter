create or replace function string_split(string text, separator text)
returns setof text
language 'sql' immutable
as $$
  select unnest(string_to_array(string, separator))
$$
