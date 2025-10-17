create or replace function refcursor_populate_recordset(base_record anyelement, in rc refcursor) returns setof anyelement language plpgsql
as
$$
begin
    loop
        fetch next from rc into base_record;
        exit when not found;
        return next base_record;
    end loop;
end 
$$
