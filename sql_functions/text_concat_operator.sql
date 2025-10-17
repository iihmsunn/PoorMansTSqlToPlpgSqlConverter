DROP OPERATOR IF EXISTS + (text, text);
DROP FUNCTION IF EXISTS op_concat_text;

CREATE FUNCTION op_concat_text(arg1 text, arg2 text)
RETURNS text AS $$
BEGIN
        RETURN arg1 || arg2;
END; $$
STABLE
LANGUAGE plpgsql;

CREATE OPERATOR + (
    leftarg = text,
    rightarg = text,
    procedure = op_concat_text,
    commutator = +
);
