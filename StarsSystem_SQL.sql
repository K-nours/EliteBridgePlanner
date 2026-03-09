select * from StarSystems
insert into StarSystems ( name,Type,Status,BridgeId,PreviousSystemId, CreatedAt,UpdatedAt)
values ( 'Mayang','TABLIER','PLANIFIE',1,NULL, SYSUTCDATETIME(),SYSUTCDATETIME())


update StarSystems set PreviousSystemId = null where id = 1
update StarSystems set PreviousSystemId = 1 where id = 2
update StarSystems set PreviousSystemId = 2 where id = 3
update StarSystems set PreviousSystemId = 3 where id = 4
update StarSystems set PreviousSystemId = 4 where id = 5
update StarSystems set PreviousSystemId = 5 where id = 2005
update StarSystems set PreviousSystemId = 2005 where id = 3004
update StarSystems set PreviousSystemId = 3004 where id = 6
select id, name,PreviousSystemId, BridgeId from StarSystems