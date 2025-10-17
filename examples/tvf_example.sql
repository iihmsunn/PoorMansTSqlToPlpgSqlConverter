
CREATE function dbo.tvf_test(@id int = null)
returns @result table(id int) as begin
    declare @_id int

    set @_id = @id

    insert into @result (id) values (@_id);

    return;
end

