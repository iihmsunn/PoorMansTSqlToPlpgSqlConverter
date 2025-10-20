/*
  comment
*/
alter procedure [dbo].[sp_request_test] (
	@username nvarchar(256),
	@sessionId nvarchar(256) = null,
	@parameters nvarchar(max), --comment
	@list dbo.type_itemsList READONLY,
	@test1 nvarchar(max) output
)
as
	select stuff(', a, b, c', 1, 1, '') as test

	create table #test1 (field1 nvarchar(max), field2 nvarchar(max))
	insert into #test1 (field1)
	exec as_trace @str = 'asd'

	select string_agg(field1, ',') within group (order by field1) as test
	from #test1

	insert into #test (a int)
	exec as_trace @str = 'asd'
	
	select (
		select
			test = '123',
			[a.b] = 1,
			[a.c] = 2,
			test2 = 66,
			[a.d.e] = 3,
			test3 = 13
		for json auto, without_array_wrapper
	) as j
	
	create table #test2 (field1 nvarchar(max), field2 nvarchar(max))
	insert into #test1 (field1, field2) values (1,2)
	insert into #test2 (field1, field2) values (3,4)

	insert into #test1 (field1)
	exec as_trace @str = 'asd'

	select
		old.fieldName as name,
	    old.fieldValue as oldValue,
	    new.fieldValue as newValue
	from #test1 unpivot (
		fieldValue for fieldName in (field2)
	) old
	left join #test2 unpivot (
		fieldValue for fieldName in (field2)
	) new on old.fieldName = new.fieldName

	select a
	from (select 1 as a) t

	declare @msg nvarchar(max) = json_value(@parameters, '$.msg')

	if isjson(@msg) = 1 begin
		set @msg = 'test'
	end

	begin try
		begin tran

		select 1 a

		if @a = 1 begin;
            throw 51000, @msg, 1;
		end

		commit
	end try
	begin catch
		rollback
		set @msg = error_message() + ', ' + cast(error_line() as nvarchar(max))
		RAISERROR (@Err, 16, 1)
	end catch
	
	select 1 + 1 as test

	select format(getdate(), 'HH:mm dd.MM.yyyy') a, format(1.2, '00.##') b

	select datepart(hour, getdate()) as test

	select datediff(hour, getdate(), dateadd(hour, 1, getdate())) as test

	select iif(1 = 1, 2, 3) as test
	
	select case when 1 = 1 then 2 else 3 end as test

	declare @t1 table (a int, b int)

	declare @t2 dbo.tu_type_items_list

	insert into @t2 (a, b) values (1, 2)
	
	insert into @t2 (a, b) select 1 a, 2 b

	insert into @t2
	exec as_trace @str = 'asd'

	select *
	from @t2 t2;

	set @username = (select 1 a from @t2)

	select *
	from (values (1, 2)) t (a, b)
	
	update t1
	set a = 1
	from #temp1 t1
	join #temp2 t2 on t1.id = t2.id
	where t1.id = 1 and (t2.type_id = 1 or t2.id = 2);

	declare @a nvarchar(max) = (select 'qwerty' as a) --comment1
			,@b int --comment2
			,@c decimal(11,2) = 10; --comment3

	create table #testStuff (
		a int,
		b nvarchar(128)
	)
		
	IF OBJECT_ID (N'tempdb..#test1', N'U') IS not NULL drop table #test1

	select 1 as a, 2 as b
	into #test2

	alter table #test2 add test int

	/*
        comment
    */
	declare @result bit = 1,
		@msg nvarchar(max) = null,
		@value nvarchar(max) = null;

	if @result = 1 begin
		set @msg = null
	end
	else if @result is null begin
		set @msg = 'unknown'
	end
	else begin
		set @msg = 'error'
	end

	declare @try_cast_test int = try_cast('123' as int);

	declare @i int = 0
	declare @result nvarchar(max) = ''
	while @i < 3 begin
		set @result = @result + cast(@i as nvarchar(max))
		set @i = @i + 1
	end

	select top(1)
		@result = 1 --comment1
		,@msg = 2 --comment2
		,3 as a --comment3
	from #temp t;

	select * from openjson(@parameters);
	
	with params
	as (
		select *
		from openjson(@parameters, '$.p') with (
				item_id1 int,
				item_id2 int '$.item_id2' as json
			)
		)
	select 
    	@result = 1,
		@msg = concat(N'Stuff done for ', item_id),
		@value = (
			select
				1 as a,
				json_query((
					select 2 as b
					for json path, without_array_wrapper
				)) as json_test
			for json path, without_array_wrapper
		)
	from params p

	select @result as result,
		@msg as msg,
		@value as value
