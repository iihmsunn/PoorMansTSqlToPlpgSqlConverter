DROP FUNCTION IF EXISTS fetch_all_from(refcursor);
CREATE FUNCTION fetch_all_from(rc refcursor)
RETURNS SETOF RECORD 
LANGUAGE plpgsql
AS $$
BEGIN
	RETURN QUERY EXECUTE 'FETCH ALL FROM ' || quote_ident($1::TEXT);
END $$;

