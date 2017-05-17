using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.Persistence.Factories;
using Umbraco.Core.Persistence.Mappers;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.Relators;
using Umbraco.Core.Persistence.SqlSyntax;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Persistence.Repositories
{
    /// <summary>
    /// Represents the UserRepository for doing CRUD operations for <see cref="IUser"/>
    /// </summary>
    internal class UserRepository : PetaPocoRepositoryBase<int, IUser>, IUserRepository
    {
        //private readonly CacheHelper _cacheHelper;

        public UserRepository(IScopeUnitOfWork work, CacheHelper cacheHelper, ILogger logger, ISqlSyntaxProvider sqlSyntax)
            : base(work, cacheHelper, logger, sqlSyntax)
        {
            //_cacheHelper = cacheHelper;
        }

        #region Overrides of RepositoryBase<int,IUser>

        protected override IUser PerformGet(int id)
        {
            var sql = GetQueryWithGroups();
            sql.Where(GetBaseWhereClause(), new { Id = id });
            sql //must be included for relator to work
                .OrderBy<UserDto>(d => d.Id, SqlSyntax)
                .OrderBy<UserGroupDto>(d => d.Id, SqlSyntax);

            var dto = Database.Fetch<UserDto, UserGroupDto, UserGroup2AppDto, UserDto>(new UserGroupRelator().Map, sql)
                .FirstOrDefault();
            
            if (dto == null)
                return null;

            var user = UserFactory.BuildEntity(dto);
            return user;
        }
        
        protected override IEnumerable<IUser> PerformGetAll(params int[] ids)
        {
            var sql = GetQueryWithGroups();
            if (ids.Any())
            {
                sql.Where("umbracoUser.id in (@ids)", new {ids = ids});
            }
            sql //must be included for relator to work
                .OrderBy<UserDto>(d => d.Id, SqlSyntax)
                .OrderBy<UserGroupDto>(d => d.Id, SqlSyntax);

            var users = ConvertFromDtos(Database.Fetch<UserDto, UserGroupDto, UserGroup2AppDto, UserDto>(new UserGroupRelator().Map, sql))
                .ToArray(); // important so we don't iterate twice, if we don't do this we can end up with null values in cache if we were caching.    
            
            return users;
        }
        
        protected override IEnumerable<IUser> PerformGetByQuery(IQuery<IUser> query)
        {
            var sqlClause = GetQueryWithGroups();
            var translator = new SqlTranslator<IUser>(sqlClause, query);
            var sql = translator.Translate();
            sql //must be included for relator to work
                .OrderBy<UserDto>(d => d.Id, SqlSyntax)
                .OrderBy<UserGroupDto>(d => d.Id, SqlSyntax);

            var dtos = Database.Fetch<UserDto, UserGroupDto, UserGroup2AppDto, UserDto>(new UserGroupRelator().Map, sql)
                .DistinctBy(x => x.Id);

            var users = ConvertFromDtos(dtos)
                .ToArray(); // important so we don't iterate twice, if we don't do this we can end up with null values in cache if we were caching.    
            
            return users;
        }
        
        #endregion

        #region Overrides of PetaPocoRepositoryBase<int,IUser>
        
        protected override Sql GetBaseQuery(bool isCount)
        {
            var sql = new Sql();
            if (isCount)
            {
                sql.Select("COUNT(*)").From<UserDto>();
            }
            else
            {
                return GetBaseQuery("*");
            }
            return sql;
        }

        /// <summary>
        /// A query to return a user with it's groups and with it's groups sections
        /// </summary>
        /// <returns></returns>
        private Sql GetQueryWithGroups()
        {
            //base query includes user groups
            var sql = GetBaseQuery("umbracoUser.*, umbracoUserGroup.*, umbracoUserGroup2App.*");
            sql.LeftJoin<User2UserGroupDto>(SqlSyntax)
                .On<User2UserGroupDto, UserDto>(SqlSyntax, dto => dto.UserId, dto => dto.Id)
                .LeftJoin<UserGroupDto>(SqlSyntax)
                .On<UserGroupDto, User2UserGroupDto>(SqlSyntax, dto => dto.Id, dto => dto.UserGroupId)
                .LeftJoin<UserGroup2AppDto>(SqlSyntax)
                .On<UserGroup2AppDto, UserGroupDto>(SqlSyntax, dto => dto.UserGroupId, dto => dto.Id);                
            return sql;
        }

        private Sql GetBaseQuery(string columns)
        {
            var sql = new Sql();
            sql.Select(columns)
                .From<UserDto>();
            return sql;
        }


        protected override string GetBaseWhereClause()
        {
            return "umbracoUser.id = @Id";
        }

        protected override IEnumerable<string> GetDeleteClauses()
        {
            var list = new List<string>
            {
                "DELETE FROM cmsTask WHERE userId = @Id",
                "DELETE FROM cmsTask WHERE parentUserId = @Id",
                "DELETE FROM umbracoUser2UserGroup WHERE userId = @Id",
                "DELETE FROM umbracoUser2NodeNotify WHERE userId = @Id",
                "DELETE FROM umbracoUser WHERE id = @Id",
                "DELETE FROM umbracoExternalLogin WHERE id = @Id"
            };
            return list;
        }

        protected override Guid NodeObjectTypeId
        {
            get { throw new NotImplementedException(); }
        }
        
        protected override void PersistNewItem(IUser entity)
        {
            var userFactory = new UserFactory();

            //ensure security stamp if non
            if (entity.SecurityStamp.IsNullOrWhiteSpace())
            {
                entity.SecurityStamp = Guid.NewGuid().ToString();
            }
            
            var userDto = userFactory.BuildDto(entity);

            var id = Convert.ToInt32(Database.Insert(userDto));
            entity.Id = id;

            entity.ResetDirtyProperties();
        }

        protected override void PersistUpdatedItem(IUser entity)
        {
            var userFactory = new UserFactory();

            //ensure security stamp if non
            if (entity.SecurityStamp.IsNullOrWhiteSpace())
            {
                entity.SecurityStamp = Guid.NewGuid().ToString();
            }

            var userDto = userFactory.BuildDto(entity);

            var dirtyEntity = (ICanBeDirty)entity;

            //build list of columns to check for saving - we don't want to save the password if it hasn't changed!
            //List the columns to save, NOTE: would be nice to not have hard coded strings here but no real good way around that
            var colsToSave = new Dictionary<string, string>()
            {
                {"userDisabled", "IsApproved"},
                {"userNoConsole", "IsLockedOut"},
                {"userType", "UserType"},
                {"startStructureID", "StartContentId"},
                {"startMediaID", "StartMediaId"},
                {"userName", "Name"},
                {"userLogin", "Username"},                
                {"userEmail", "Email"},                
                {"userLanguage", "Language"},
                {"securityStampToken", "SecurityStamp"},
                {"lastLockoutDate", "LastLockoutDate"},
                {"lastPasswordChangeDate", "LastPasswordChangeDate"},
                {"lastLoginDate", "LastLoginDate"},
                {"failedLoginAttempts", "FailedPasswordAttempts"},
            };

            //create list of properties that have changed
            var changedCols = colsToSave
                .Where(col => dirtyEntity.IsPropertyDirty(col.Value))
                .Select(col => col.Key)
                .ToList();

            // DO NOT update the password if it has not changed or if it is null or empty
            if (dirtyEntity.IsPropertyDirty("RawPasswordValue") && entity.RawPasswordValue.IsNullOrWhiteSpace() == false)
            {
                changedCols.Add("userPassword");

                //special case - when using ASP.Net identity the user manager will take care of updating the security stamp, however
                // when not using ASP.Net identity (i.e. old membership providers), we'll need to take care of updating this manually
                // so we can just detect if that property is dirty, if it's not we'll set it manually
                if (dirtyEntity.IsPropertyDirty("SecurityStamp") == false)
                {
                    userDto.SecurityStampToken = entity.SecurityStamp = Guid.NewGuid().ToString();
                    changedCols.Add("securityStampToken");
                }
            }

            //only update the changed cols
            if (changedCols.Count > 0)
            {
                Database.Update(userDto, changedCols);
            }
            
            //lookup all assigned
            var assigned = entity.Groups == null || entity.Groups.Any() == false
                ? new List<UserGroupDto>()
                : Database.Fetch<UserGroupDto>("SELECT * FROM umbracoUserGroup WHERE userGroupAlias IN (@aliases)", new {aliases = entity.Groups});
            
            //first delete all 
            //TODO: We could do this a nicer way instead of "Nuke and Pave"
            Database.Delete<User2UserGroupDto>("WHERE UserId = @UserId", new { UserId = entity.Id });

            foreach (var groupDto in assigned)
            {
                var dto = new User2UserGroupDto
                {
                    UserGroupId = groupDto.Id,
                    UserId = entity.Id
                };
                Database.Insert(dto);
            }

            entity.ResetDirtyProperties();
        }

        #endregion

        #region Implementation of IUserRepository

        public int GetCountByQuery(IQuery<IUser> query)
        {
            var sqlClause = GetBaseQuery("umbracoUser.id");
            var translator = new SqlTranslator<IUser>(sqlClause, query);
            var subquery = translator.Translate();
            //get the COUNT base query
            var sql = GetBaseQuery(true)
                .Append(new Sql("WHERE umbracoUser.id IN (" + subquery.SQL + ")", subquery.Arguments));

            return Database.ExecuteScalar<int>(sql);
        }

        public bool Exists(string username)
        {
            var sql = new Sql();

            sql.Select("COUNT(*)")
                .From<UserDto>()
                .Where<UserDto>(x => x.UserName == username);

            return Database.ExecuteScalar<int>(sql) > 0;
        }
        
        /// <summary>
        /// Gets a list of <see cref="IUser"/> objects associated with a given group
        /// </summary>
        /// <param name="groupId">Id of group</param>
        public IEnumerable<IUser> GetAllInGroup(int groupId)
        {
            return GetAllInOrNotInGroup(groupId, true);
        }

        /// <summary>
        /// Gets a list of <see cref="IUser"/> objects not associated with a given group
        /// </summary>
        /// <param name="groupId">Id of group</param>
        public IEnumerable<IUser> GetAllNotInGroup(int groupId)
        {
            return GetAllInOrNotInGroup(groupId, false);
        }

        private IEnumerable<IUser> GetAllInOrNotInGroup(int groupId, bool include)
        {
            var sql = new Sql();
            sql.Select("*")
                .From<UserDto>();

            var innerSql = new Sql();
            innerSql.Select("umbracoUser.id")
                .From<UserDto>()
                .LeftJoin<User2UserGroupDto>()
                .On<UserDto, User2UserGroupDto>(left => left.Id, right => right.UserId)
                .Where("umbracoUser2UserGroup.userGroupId = " + groupId);

            sql.Where(string.Format("umbracoUser.id {0} ({1})",
                include ? "IN" : "NOT IN",
                innerSql.SQL));
            return ConvertFromDtos(Database.Fetch<UserDto>(sql));
        }

        [Obsolete("Use the overload with long operators instead")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public IEnumerable<IUser> GetPagedResultsByQuery(IQuery<IUser> query, int pageIndex, int pageSize, out int totalRecords, Expression<Func<IUser, string>> orderBy)
        {
            if (orderBy == null) throw new ArgumentNullException("orderBy");

            // get the referenced column name and find the corresp mapped column name
            var expressionMember = ExpressionHelper.GetMemberInfo(orderBy);
            var mapper = MappingResolver.Current.ResolveMapperByType(typeof(IUser));
            var mappedField = mapper.Map(expressionMember.Name);

            if (mappedField.IsNullOrWhiteSpace())
                throw new ArgumentException("Could not find a mapping for the column specified in the orderBy clause");
            
            long tr;
            var results = GetPagedResultsByQuery(query, Convert.ToInt64(pageIndex), pageSize, out tr, mappedField, Direction.Ascending);
            totalRecords = Convert.ToInt32(tr);
            return results;
        }

        /// <summary>
        /// Gets paged user results
        /// </summary>
        /// <param name="query"></param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="totalRecords"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="userGroups">Optional parameter to filter by specified user groups</param>
        /// <param name="userState">Optional parameter to filter by specfied user state</param>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <remarks>
        /// The query supplied will ONLY work with data specifically on the umbracoUser table because we are using PetaPoco paging (SQL paging)
        /// </remarks>
        public IEnumerable<IUser> GetPagedResultsByQuery(IQuery<IUser> query, long pageIndex, int pageSize, out long totalRecords, Expression<Func<IUser, object>> orderBy, Direction orderDirection, string[] userGroups = null, UserState? userState = null, IQuery<IUser> filter = null)
        {
            if (orderBy == null) throw new ArgumentNullException("orderBy");

            // get the referenced column name and find the corresp mapped column name
            var expressionMember = ExpressionHelper.GetMemberInfo(orderBy);
            var mapper = MappingResolver.Current.ResolveMapperByType(typeof(IUser));
            var mappedField = mapper.Map(expressionMember.Name);

            if (mappedField.IsNullOrWhiteSpace())
                throw new ArgumentException("Could not find a mapping for the column specified in the orderBy clause");

            return GetPagedResultsByQuery(query, pageIndex, pageSize, out totalRecords, mappedField, orderDirection, userGroups, userState, filter);
        }

        private IEnumerable<IUser> GetPagedResultsByQuery(IQuery<IUser> query, long pageIndex, int pageSize, out long totalRecords, string orderBy, Direction orderDirection, string[] userGroups = null, UserState? userState = null, IQuery<IUser> filter = null)
        {
            if (string.IsNullOrWhiteSpace(orderBy)) throw new ArgumentException("Value cannot be null or whitespace.", "orderBy");
            

            var filterSql = new Sql();
            if (filter != null)
            {
                foreach (var filterClaus in filter.GetWhereClauses())
                {
                    filterSql.Append(string.Format("AND ({0})", filterClaus.Item1), filterClaus.Item2);
                }
            }
            Func<Tuple<string, object[]>> filterCallback = () => new Tuple<string, object[]>(filterSql.SQL, filterSql.Arguments);
            
            // Get base query for returning IDs
            var sqlBaseIds = GetBaseQuery("id");
            // Get base query for returning all data
            var sqlBaseFull = GetBaseQuery(false);

            if (query == null) query = new Query<IUser>();
            var translatorIds = new SqlTranslator<IUser>(sqlBaseIds, query);
            var sqlQueryIds = translatorIds.Translate();
            var translatorFull = new SqlTranslator<IUser>(sqlBaseFull, query);
            var sqlQueryFull = translatorFull.Translate();            

            //get sorted and filtered sql
            var sqlNodeIdsWithSort = GetSortedSqlForPagedResults(
                GetFilteredSqlForPagedResults(sqlQueryIds, filterCallback),
                orderDirection, orderBy);

            // Get page of results and total count
            IEnumerable<IUser> result;
            var pagedResult = Database.Page<UserDto>(pageIndex + 1, pageSize, sqlNodeIdsWithSort);
            totalRecords = Convert.ToInt32(pagedResult.TotalItems);

            return Enumerable.Empty<IUser>();

            ////NOTE: We need to check the actual items returned, not the 'totalRecords', that is because if you request a page number
            //// that doesn't actually have any data on it, the totalRecords will still indicate there are records but there are none in
            //// the pageResult, then the GetAll will actually return ALL records in the db.
            //if (pagedResult.Items.Any())
            //{
            //    //Create the inner paged query that was used above to get the paged result, we'll use that as the inner sub query
            //    var args = sqlNodeIdsWithSort.Arguments;
            //    string sqlStringCount, sqlStringPage;
            //    Database.BuildPageQueries<UserDto>(pageIndex * pageSize, pageSize, sqlNodeIdsWithSort.SQL, ref args, out sqlStringCount, out sqlStringPage);

            //    //We need to make this FULL query an inner join on the paged ID query
            //    var splitQuery = sqlQueryFull.SQL.Split(new[] { "WHERE " }, StringSplitOptions.None);
            //    var fullQueryWithPagedInnerJoin = new Sql(splitQuery[0])
            //        .Append("INNER JOIN (")
            //        //join the paged query with the paged query arguments
            //        .Append(sqlStringPage, args)
            //        .Append(") temp ")
            //        .Append(string.Format("ON {0}.{1} = temp.{1}", nodeIdSelect.Item1, nodeIdSelect.Item2))
            //        //add the original where clause back with the original arguments
            //        .Where(splitQuery[1], sqlQueryIds.Arguments);

            //    //get sorted and filtered sql
            //    var fullQuery = GetSortedSqlForPagedResults(
            //        GetFilteredSqlForPagedResults(fullQueryWithPagedInnerJoin, defaultFilter),
            //        orderDirection, orderBy, orderBySystemField, nodeIdSelect);

            //    return processQuery(fullQuery, new PagingSqlQuery<TDto>(Database, sqlNodeIdsWithSort, pageIndex, pageSize));
            //}
            //else
            //{
            //    result = Enumerable.Empty<TEntity>();
            //}

            //return result;


            //return GetPagedResultsByQuery<MemberDto>(query, pageIndex, pageSize, out totalRecords,
            //    new Tuple<string, string>("cmsMember", "nodeId"),
            //    (sqlFull, sqlIds) => ProcessQuery(sqlFull, sqlIds), orderBy, orderDirection, orderBySystemField,
            //    filterCallback);


            //var sql = new Sql()
            //    .Select("umbracoUser.Id")
            //    .From<UserDto>(SqlSyntax);

            //var idsQuery = query == null ? sql : new SqlTranslator<IUser>(sql, query).Translate();

            //// need to ensure the order by is in brackets, see: https://github.com/toptensoftware/PetaPoco/issues/177
            //idsQuery.OrderBy("(" + orderBy + ")");
            //var page = Database.Page<int>(pageIndex + 1, pageSize, idsQuery);
            //totalRecords = Convert.ToInt32(page.TotalItems);

            //if (totalRecords == 0)
            //    return Enumerable.Empty<IUser>();

            //// now get the actual users and ensure they are ordered properly (same clause)
            //var ids = page.Items.ToArray();
            //return ids.Length == 0 ? Enumerable.Empty<IUser>() : GetAll(ids).OrderBy(orderBy.Compile());
        }

        private Sql GetFilteredSqlForPagedResults(Sql sql, Func<Tuple<string, object[]>> defaultFilter = null)
        {
            Sql filteredSql;

            // Apply filter
            if (defaultFilter != null)
            {
                var filterResult = defaultFilter();

                //NOTE: this is certainly strange - NPoco handles this much better but we need to re-create the sql
                // instance a couple of times to get the parameter order correct, for some reason the first
                // time the arguments don't show up correctly but the SQL argument parameter names are actually updated
                // accordingly - so we re-create it again. In v8 we don't need to do this and it's already taken care of.

                filteredSql = new Sql(sql.SQL, sql.Arguments);
                var args = filteredSql.Arguments.Concat(filterResult.Item2).ToArray();
                filteredSql = new Sql(
                    string.Format("{0} {1}", filteredSql.SQL, filterResult.Item1),
                    args);
                filteredSql = new Sql(filteredSql.SQL, args);
            }
            else
            {
                //copy to var so that the original isn't changed
                filteredSql = new Sql(sql.SQL, sql.Arguments);
            }
            return filteredSql;
        }

        private Sql GetSortedSqlForPagedResults(Sql sql, Direction orderDirection, string orderBy)
        {
            //copy to var so that the original isn't changed
            var sortedSql = new Sql(sql.SQL, sql.Arguments);

            // Apply order according to parameters
            if (string.IsNullOrEmpty(orderBy) == false)
            {
                //each order by param needs to be in a bracket! see: https://github.com/toptensoftware/PetaPoco/issues/177
                var orderByParams = new[] { string.Format("({0})", orderBy) };
                if (orderDirection == Direction.Ascending)
                {
                    sortedSql.OrderBy(orderByParams);
                }
                else
                {
                    sortedSql.OrderByDescending(orderByParams);
                }
            }
            return sortedSql;
        }

        internal IEnumerable<IUser> GetNextUsers(int id, int count)
        {
            var idsQuery = new Sql()
                .Select("umbracoUser.id")
                .From<UserDto>(SqlSyntax)
                .Where<UserDto>(x => x.Id >= id)
                .OrderBy<UserDto>(x => x.Id, SqlSyntax);

            // first page is index 1, not zero
            var ids = Database.Page<int>(1, count, idsQuery).Items.ToArray();

            // now get the actual users and ensure they are ordered properly (same clause)
            return ids.Length == 0 ? Enumerable.Empty<IUser>() : GetAll(ids).OrderBy(x => x.Id);
        }

        #endregion

        private IEnumerable<IUser> ConvertFromDtos(IEnumerable<UserDto> dtos)
        {
            return dtos.Select(UserFactory.BuildEntity);
        }

        //private IEnumerable<IUserGroup> ConvertFromDtos(IEnumerable<UserGroupDto> dtos)
        //{
        //    return dtos.Select(dto =>
        //    {
        //        var userGroupFactory = new UserGroupFactory();
        //        return userGroupFactory.BuildEntity(dto);
        //    });
        //}
    }
}