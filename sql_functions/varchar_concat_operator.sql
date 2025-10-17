DROP OPERATOR IF EXISTS + (varchar, varchar);
DROP FUNCTION IF EXISTS op_concat_varchar;

CREATE FUNCTION op_concat_varchar(arg1 varchar, arg2 varchar)
RETURNS varchar AS $$
BEGIN
        RETURN arg1 || arg2;
END; $$
STABLE
LANGUAGE plpgsql;

CREATE OPERATOR + (
    leftarg = varchar,
    rightarg = varchar,
    procedure = op_concat_varchar,
    commutator = +
);
