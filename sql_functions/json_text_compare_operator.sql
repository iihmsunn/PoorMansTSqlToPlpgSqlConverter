DROP OPERATOR IF EXISTS = (json, text);
DROP FUNCTION IF EXISTS op_eq_json_text;

CREATE FUNCTION op_eq_json_text(arg1 json, arg2 text)
RETURNS boolean AS $$
BEGIN
    RETURN arg1::text = arg2;
END; $$
STABLE
LANGUAGE plpgsql;

CREATE OPERATOR = (
    leftarg = json,
    rightarg = text,
    procedure = op_eq_json_text,
    commutator = =
);