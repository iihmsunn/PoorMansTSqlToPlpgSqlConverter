
CREATE function dbo.tvf_test(@id int = null)
returns nvarchar(max) as begin
    declare @_id int

    set @_id = @id

    return @_id
end

