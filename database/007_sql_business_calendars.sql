ALTER TABLE dbo.sla_calendars ADD sql_timezone nvarchar(80) NULL;
GO
UPDATE dbo.sla_calendars SET sql_timezone=N'India Standard Time' WHERE timezone=N'Asia/Kolkata';
UPDATE dbo.sla_calendars SET sql_timezone=N'UTC' WHERE timezone IN(N'UTC',N'Etc/UTC');
GO
CREATE FUNCTION dbo.calendar_instant(@day date,@clock time(7),@zone nvarchar(80)) RETURNS datetimeoffset(7) AS
BEGIN
 DECLARE @local datetime2(7)=DATEADD(NANOSECOND,DATEPART(NANOSECOND,@clock),DATEADD(SECOND,DATEDIFF(SECOND,CONVERT(time,'00:00:00'),@clock),CONVERT(datetime2(7),@day)));
 DECLARE @instant datetimeoffset(7)=@local AT TIME ZONE @zone;
 DECLARE @later int=DATEPART(TZOFFSET,DATEADD(HOUR,6,@local) AT TIME ZONE @zone);
 DECLARE @trial datetimeoffset(7)=TODATETIMEOFFSET(@local,@later);
 -- SQL normally chooses the earlier daylight offset in a fold. PostgreSQL
 -- chooses the standard side. A later-offset candidate is valid only if its
 -- conversion back to local wall time exactly matches the requested time.
 IF @later<DATEPART(TZOFFSET,@instant) AND CONVERT(datetime2(7),@trial AT TIME ZONE @zone)=@local SET @instant=@trial;
 RETURN SWITCHOFFSET(@instant,'+00:00');
END;
GO
CREATE FUNCTION dbo.sla_business_minutes_between(@calendar uniqueidentifier,@start datetimeoffset(7),@end datetimeoffset(7)) RETURNS int AS
BEGIN
 IF @end<=@start RETURN 0;
 DECLARE @zone nvarchar(80),@days nvarchar(max),@opening time(7),@closing time(7);
 SELECT @zone=sql_timezone,@days=working_days,@opening=work_start,@closing=work_end FROM dbo.sla_calendars WHERE id=@calendar;
 IF @zone IS NULL RETURN NULL;
 DECLARE @day date=CONVERT(date,@start AT TIME ZONE @zone),@final date=CONVERT(date,@end AT TIME ZONE @zone),@total int=0,@guard int=0;
 WHILE @day<=@final
 BEGIN
  SET @guard=@guard+1; IF @guard>3700 RETURN NULL;
  DECLARE @dow int=((DATEDIFF(DAY,CONVERT(date,'19000101'),@day)%7)+7)%7+1;
  IF EXISTS(SELECT 1 FROM OPENJSON(@days) WHERE TRY_CONVERT(int,[value])=@dow) AND NOT EXISTS(SELECT 1 FROM dbo.sla_holidays WHERE calendar_id=@calendar AND holiday_date=@day)
  BEGIN
   DECLARE @beginning datetimeoffset(7)=dbo.calendar_instant(@day,@opening,@zone),@ending datetimeoffset(7)=dbo.calendar_instant(@day,@closing,@zone);
   DECLARE @a datetimeoffset(7)=CASE WHEN @start>@beginning THEN @start ELSE @beginning END,@b datetimeoffset(7)=CASE WHEN @end<@ending THEN @end ELSE @ending END;
   IF @b>@a SET @total=@total+CONVERT(int,DATEDIFF_BIG(MICROSECOND,@a,@b)/60000000);
  END;
  SET @day=DATEADD(DAY,1,@day);
 END;
 RETURN @total;
END;
GO
CREATE FUNCTION dbo.sla_add_business_minutes(@calendar uniqueidentifier,@start datetimeoffset(7),@requested int) RETURNS datetimeoffset(7) AS
BEGIN
 DECLARE @cursor datetimeoffset(7)=@start,@remaining int=CASE WHEN @requested>0 THEN @requested ELSE 0 END;
 IF @remaining=0 RETURN @cursor;
 DECLARE @zone nvarchar(80),@days nvarchar(max),@opening time(7),@closing time(7);
 SELECT @zone=sql_timezone,@days=working_days,@opening=work_start,@closing=work_end FROM dbo.sla_calendars WHERE id=@calendar;
 IF @zone IS NULL RETURN NULL;
 DECLARE @guard int=0;
 WHILE @remaining>0
 BEGIN
  SET @guard=@guard+1; IF @guard>3700 RETURN NULL;
  DECLARE @day date=CONVERT(date,@cursor AT TIME ZONE @zone),@dow int;
  SET @dow=((DATEDIFF(DAY,CONVERT(date,'19000101'),@day)%7)+7)%7+1;
  IF EXISTS(SELECT 1 FROM OPENJSON(@days) WHERE TRY_CONVERT(int,[value])=@dow) AND NOT EXISTS(SELECT 1 FROM dbo.sla_holidays WHERE calendar_id=@calendar AND holiday_date=@day)
  BEGIN
   DECLARE @beginning datetimeoffset(7)=dbo.calendar_instant(@day,@opening,@zone),@ending datetimeoffset(7)=dbo.calendar_instant(@day,@closing,@zone);
   IF @cursor<@beginning SET @cursor=@beginning;
   IF @cursor<@ending
   BEGIN
    DECLARE @available int=CONVERT(int,DATEDIFF_BIG(MICROSECOND,@cursor,@ending)/60000000);
    IF @remaining<=@available RETURN DATEADD(MINUTE,@remaining,@cursor);
    SET @remaining=@remaining-@available;
   END;
  END;
  SET @cursor=dbo.calendar_instant(DATEADD(DAY,1,@day),CONVERT(time,'00:00:00'),@zone);
 END;
 RETURN @cursor;
END;
GO
