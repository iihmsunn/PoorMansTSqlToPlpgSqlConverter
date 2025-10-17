create or replace function str(val float, len int, decimal_len int) 
returns text
as $$
select left(to_char(val, concat('FM', repeat('9', len - 2), '0D', repeat('9', decimal_len))), len);
$$ language sql immutable;
