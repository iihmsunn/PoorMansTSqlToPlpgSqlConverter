DROP OPERATOR IF EXISTS = (boolean, int);
DROP FUNCTION IF EXISTS op_eq_boolean_int;

CREATE FUNCTION op_eq_boolean_int(arg1 boolean, arg2 int)
RETURNS boolean AS $$
BEGIN
    RETURN CASE
    WHEN arg1 and arg2 = 1
    THEN true
    WHEN not arg1 and arg2 = 0
    THEN true
    ELSE false
    END;
END; $$
STABLE
LANGUAGE plpgsql;

CREATE OPERATOR = (
    leftarg = boolean,
    rightarg = int,
    procedure = op_eq_boolean_int,
    commutator = =
);
