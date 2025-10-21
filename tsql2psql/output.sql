/*
  comment
*/
replace procedure "dbo"."sp_request_test" (
	_username text,
	_session_id text = null,
	_parameters text, --comment
	_list type_items_list[],
	out _test1 text
	)
language 'plpgsql'
as $$
declare
	_select1 refcursor;
	_select2 refcursor;
	_select3 refcursor;
	_select4 refcursor;
	_select5 refcursor;
	_select6 refcursor;
	_select7 refcursor;
	_msg text;
	_select8 refcursor;
	_select9 refcursor;
	_select10 refcursor;
	_select11 refcursor;
	_select12 refcursor;
	_select13 refcursor;
	_select14 refcursor;
	_t2 tu_type_items_list[];
	_select15 refcursor;
	_select16 refcursor;
	_a text;  --comment1
	_b int;  --comment2
	_c numeric(11, 2);  --comment3
	_result boolean;
	_msg text;
	_value text;
	_try_cast_test int;
	_i int;
	_result text;
	_select17 refcursor;
	_select18 refcursor;
begin
	create temp table _new_ids (new_id bigint);

	create temp table _test (
		id bigint primary key identity(1, 1),
		val text
		);

	delete
	from _test
	where id = 1;

	delete
	from _test
	where id = 1;

	with data
	as (
		select 'test1' val
		
		union
		
		select 'test2' val
		),
	temp
	as (
		select *
		from data
		),
	inserted
	as (
		insert into _test (val)
		select val
		from temp
		returning id
		)
	insert into _new_ids(new_id)
	select id
	from inserted;

	_select1 := 'sp_request_test__select1';

	open _select1 for
	select overlay(', a, b, c' placing '' from 1 for 1) as test;

	create temp table _test1 (
		field1 text,
		field2 text
		);

	call as_trace(_str => 'asd');

	insert into _test1 (field1)
	select *
	from fetch_all_from('as_trace_select1') as (
			field1 text,
			field2 text
			);

	_select2 := 'sp_request_test__select2';

	open _select2 for
	select (
			select string_agg(field1, ',')
			from _test
			) as test1;

	_select3 := 'sp_request_test__select3';

	open _select3 for
	select (
			select string_agg(field1 + ' ', '')
			from _test
			) as test2;

	_select4 := 'sp_request_test__select4';

	open _select4 for
	select string_agg(field1, ',' order by field1)
	from _test1;

	call as_trace(_str => 'asd');

	insert into _test (a int)
	select *
	from fetch_all_from('as_trace_select1') as (
			id bigint primary key identity(1, 1),
			val text
			);

	_select5 := 'sp_request_test__select5';

	open _select5 for
	select (
			select to_json(_to_json_temp) /*CONVERTER WARNING: converted from /for json auto/ as if it was /for json path/. Output is potentially different, especially if query has joins*/
			from (
				select '123' as test,
					66 as test2,
					13 as test3,
					json_object('b': 1, 'c': 2, 'd': json_object('e': 3)) as a
				) _to_json_temp
			) as j;

	create temp table _test2 (
		field1 text,
		field2 text
		);

	insert into _test1 (
		field1,
		field2
		)
	values (
		1,
		2
		);

	insert into _test2 (
		field1,
		field2
		)
	values (
		3,
		4
		);

	call as_trace(_str => 'asd');

	insert into _test1 (field1)
	select *
	from fetch_all_from('as_trace_select1') as (
			field1 text,
			field2 text
			);

	_select6 := 'sp_request_test__select6';

	open _select6 for
	select old.field_name as name,
		old.field_value as old_value,
		new.field_value as new_value
	from (
		select unnest(array['field2']) as field_name,
			unnest(array[field2]) as field_value
		from _test1
		) old
	left join (
		select unnest(array['field2']) as field_name,
			unnest(array[field2]) as field_value
		from _test2
		) new on old.field_name = new.field_name;

	_select7 := 'sp_request_test__select7';

	open _select7 for
	select a
	from (
		select 1 as a
		) t;

	_msg := json_value(_parameters, '$.msg');

	if (_msg is json) = 1
	then
		_msg := 'test';
	end if;

	begin
		_select8 := 'sp_request_test__select8';

		open _select8 for
		select 1 a;

		if _a = 1
		then
			raise exception _msg using errcode = 'T0000';
		end if;

	exception when others then
		_msg := error_message() + ', ' + error_line()::text;

		raise exception __err using errcode = 'T0000';
	end;

	_select9 := 'sp_request_test__select9';

	open _select9 for
	select 1 + 1 as test;

	_select10 := 'sp_request_test__select10';

	open _select10 for
	select to_char(now(), 'HH:MI dd.MM.yyyy') a,
		to_char(1.2, 'FM00D99') b;

	_select11 := 'sp_request_test__select11';

	open _select11 for
	select extract(hour from now()) as test;

	_select12 := 'sp_request_test__select12';

	open _select12 for
	select extract(hour from (now() + interval '1 hour') - now()) as test;

	_select13 := 'sp_request_test__select13';

	open _select13 for
	select case 
			when 1 = 1
				then 2,
			else 3
			end as test;

	_select14 := 'sp_request_test__select14';

	open _select14 for
	select case 
			when 1 = 1
				then 2
			else 3
			end as test;

	create temp table _t1 (
		a int,
		b int
		);

	_t2 := array_cat(_t2, (
				select array_agg(_t)
				from (
					values (
						1,
						2
						)
					) _t
				));

	_t2 := array_cat(_t2, (
				select array_agg(_t)
				from (
					select 1 a,
						2 b
					) _t
				));

	call as_trace(_str => 'asd');

	_t2 := array_cat(_t2, (
				select array_agg(_t)
				from (
					select *
					from refcursor_populate_recordset(null::tu_type_items_list, 'as_trace_select1')
					) _t
				));

	_select15 := 'sp_request_test__select15';

	open _select15 for
	select *
	from _t2 t2;

	_username := (
			select 1 a
			from _t2
			);

	_select16 := 'sp_request_test__select16';

	open _select16 for
	select *
	from (
		values (
			1,
			2
			)
		) t(a, b);

	update _temp1 as __target__
	set a = 1
	from _temp1 t1
	join _temp2 t2 on t1.id = t2.id
	where (
			t1.id = 1
			and (
				t2.type_id = 1
				or t2.id = 2
				)
			)
		and __target__.ctid = t1.ctid;

	_a := (
			select 'qwerty' as a
			);
	_c := 10;

	create temp table _test_stuff (
		a int,
		b text
		);

	drop table if exists _test1;

	create temp table _test2 as
	select 1 as a,
		2 as b;

	alter table _test2 add test int;

	/*
        comment
    */
	_result := 1;
	_msg := null;
	_value := null;

	if _result = 1
	then
		_msg := null;
	elsif _result is null
	then
		_msg := 'unknown';
	else
		_msg := 'error';
	end if;

	_try_cast_test := try_cast('123', null::int);

	_i := 0;

	_result := '';

	while _i < 3
	loop
		_result := _result + _i::text;

		_i := _i + 1;
	end loop;

	select 1 as _result, --comment1
		2 as _msg, --comment2
		3 as a --comment3
	into _result,
		_msg
	from _temp t
	limit (1);

	_select17 := 'sp_request_test__select17';

	open _select17 for
	select *
	from json_each(_parameters);

	with params
	as (
		select *
		from json_table (
				_parameters,
				'$.p' columns (
						item_id1 int,
						item_id2 int path '$.item_id2' with wrapper
						)
				)
		)
	select 1 as _result,
		concat (
			N'Stuff done for ',
			item_id
			) as _msg,
		(
			select to_json(_to_json_temp)
			from (
				select 1 as a,
					(
						select to_json(_to_json_temp)
						from (
							select 2 as b,
							) _to_json_temp
						)::json as json_test,
				) _to_json_temp
			) as _value
	into _result,
		_msg,
		_value
	from params p;

	_select18 := 'sp_request_test__select18';

	open _select18 for
	select _result as result,
		_msg as msg,
		_value as value;
end;
$$;
