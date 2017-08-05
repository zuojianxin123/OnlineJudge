﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JoyOI.OnlineJudge.Models;
using JoyOI.OnlineJudge.WebApi.Models;

namespace JoyOI.OnlineJudge.WebApi.Controllers.Api
{
    [Route("api/[controller]")]
    public class GroupController : BaseController
    {
        #region Group
        [HttpGet("all")]
        [HttpGet("all/page/{page:int}")]
        public Task<ApiResult<PagedResult<IEnumerable<Group>>>> Get(GroupType? type, string name, int? page, CancellationToken token)
        {
            IQueryable<Group> ret = DB.Groups;
            if (type.HasValue)
            {
                ret = ret.Where(x => x.Type == type.Value);
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                ret = ret.Where(x => x.Name.Contains(name) || name.Contains(x.Name));
            }
            if (!page.HasValue)
            {
                page = 1;
            }
            return Paged(ret, page.Value, 20, token);
        }

        [HttpGet("{id:(^[a-zA-Z0-9-_ ]{4,128}$)}")]
        public async Task<ApiResult<Group>> Get(string id, CancellationToken token)
        {
            var ret = await DB.Groups.SingleOrDefaultAsync(x => x.Id == id, token);
            return Result(ret);
        }

        [HttpPost("{id:(^[a-zA-Z0-9-_ ]{4,128}$)}")]
        [HttpPatch("{id:(^[a-zA-Z0-9-_ ]{4,128}$)}")]
        public async Task<ApiResult> Patch(string id, [FromBody]string value, CancellationToken token)
        {
            if (!await HasPermissionToGroupAsync(id, token))
            {
                return Result(401, "No permission");
            }
            else
            {
                var group = await DB.Groups.SingleOrDefaultAsync(x => x.Id == id, token);
                if (group == null)
                {
                    return Result(404, "Not Found");
                }

                PatchEntity(group, value);
                await DB.SaveChangesAsync(token);
                return Result(200, "Patch Succeeded");
            }
        }
        
        [HttpPut("{id:(^[a-zA-Z0-9-_ ]{4,128}$)}")]
        public async Task<ApiResult> Put(string id, [FromBody]string value, CancellationToken token)
        {
            if (await DB.Groups.AnyAsync(x => x.Id == id, token))
            {
                return Result(403, "The problem id is already exists.");
            }

            if (await DB.UserClaims.CountAsync(x => x.ClaimType == Constants.GroupEditPermission
                && x.ClaimValue == id
                && x.UserId == User.Current.Id, token) >= 5)
            {
                return Result(400, "You cannot own more than 5 groups.");
            }

            var group = PutEntity<Group>(value).Entity;
            group.Id = id;
            group.CachedMemberCount = 1;
            DB.Groups.Add(group);

            DB.GroupMembers.Add(new GroupMember
            {
                CreatedTime = DateTime.Now,
                GroupId = group.Id,
                Status = GroupMemberStatus.Approved,
                UserId = User.Current.Id
            });

            await DB.SaveChangesAsync(token);
            return Result(200, "Put Succeeded");
        }

        [HttpDelete("{id:(^[a-zA-Z0-9-_ ]{4,128}$)}")]
        public async Task<ApiResult> Delete(string id, CancellationToken token)
        {
            if (!await HasPermissionToGroupAsync(id, token))
            {
                return Result(401, "No Permission");
            }
            else
            {
                await DB.UserClaims
                    .Where(x => x.ClaimType == Constants.GroupEditPermission)
                    .Where(x => x.ClaimValue == id)
                    .DeleteAsync(token);
                await DB.Groups
                    .Where(x => x.Id == id)
                    .DeleteAsync(token);
                return Result(200, "Delete Succeeded");
            }
        }
        #endregion

        #region Group Member
        [HttpGet("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/member/all")]
        public Task<ApiResult<PagedResult<IEnumerable<GroupMember>>>> GetMember(string groupId, GroupMemberStatus? status, int? page, CancellationToken token)
        {
            IQueryable<GroupMember> ret = DB.GroupMembers
                .Where(x => x.GroupId == groupId);

            if (status.HasValue)
            {
                ret = ret.Where(x => x.Status == status.Value);
            }

            ret = ret.OrderBy(x => x.CreatedTime);

            if (!page.HasValue)
            {
                page = 1;
            }

            return Paged(ret, page.Value, 20, token);
        }
        
        [HttpPut("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/member/{userId:Guid?}")]
        public async Task<ApiResult> PutMember(string groupId, Guid? userId, [FromBody] string value, CancellationToken token)
        {
            if (!await DB.Groups.AnyAsync(x => x.Id == groupId, token))
            {
                return Result(404, "Group Not Found");
            }
            else if (userId.HasValue && userId.Value != User.Current.Id && !await HasPermissionToGroupAsync(groupId))
            {
                return Result(401, "No permission");
            }
            else
            {
                var groupMember = PutEntity<GroupMember>(value).Entity;
                groupMember.GroupId = groupId;
                if (userId.HasValue)
                {
                    groupMember.UserId = userId.Value;
                    groupMember.Status = GroupMemberStatus.Approved;
                }
                else
                {
                    groupMember.UserId = User.Current.Id;
                }

                groupMember.CreatedTime = DateTime.Now;
                DB.GroupMembers.Add(groupMember);
                await DB.SaveChangesAsync(token);
                return Result(200, "Put Succeeded");
            }
        }

        [HttpPost("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/member/{userId:Guid}")]
        [HttpPatch("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/member/{userId:Guid}")]
        public async Task<ApiResult> PatchMember(string groupId, Guid userId, [FromBody] string value, CancellationToken token)
        {
            if (!await HasPermissionToGroupAsync(groupId, token))
            {
                return Result(401, "No permission");
            }

            var groupMember = await DB.GroupMembers.SingleOrDefaultAsync(x => x.GroupId == groupId && x.UserId == userId, token);
            if (groupMember == null)
            {
                return Result(404, "Member Not Found");
            }

            PatchEntity(groupMember, value);
            await DB.SaveChangesAsync(token);
            return Result(200, "Patch Succeeded");
        }

        [HttpDelete("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/member/{userId:Guid?}")]
        public async Task<ApiResult> DeleteMember(string groupId, Guid? userId, CancellationToken token)
        {
            if (userId.HasValue && userId.Value != User.Current.Id && !await HasPermissionToGroupAsync(groupId, token))
            {
                return Result(401, "No permission");
            }

            if (!userId.HasValue)
            {
                userId = User.Current.Id;
            }

            if (await DB.UserClaims.AnyAsync(x => x.ClaimType == Constants.GroupEditPermission && x.ClaimValue == groupId && x.UserId == userId.Value, token))
            {
                return Result(400, "Cannot remove an owner from the group");
            }

            await DB.GroupMembers
                .Where(x => x.UserId == userId.Value)
                .Where(x => x.GroupId == groupId)
                .DeleteAsync(token);

            return Result(200, "Delete succeeded");
        }
        #endregion

        #region Claims
        [HttpGet("{problemid:(^[a-zA-Z0-9-_ ]{4,128}$)}/claim/all")]
        public async Task<ApiResult<List<IdentityUserClaim<Guid>>>> GetClaims(string problemId, CancellationToken token)
        {
            var ret = await DB.UserClaims
                .Where(x => x.ClaimType == Constants.ProblemEditPermission)
                .Where(x => x.ClaimValue == problemId)
                .ToListAsync(token);
            return Result(ret);
        }

        [HttpPut("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/claim")]
        public async Task<ApiResult> PutClaims(string groupId, [FromBody] IdentityUserClaim<Guid> value, CancellationToken token)
        {
            if (!await HasPermissionToGroupAsync(groupId, token))
            {
                return Result(401, "No permission");
            }
            else if (await DB.UserClaims.AnyAsync(x => x.ClaimValue == groupId && x.ClaimType == Constants.GroupEditPermission && x.UserId == value.UserId, token))
            {
                return Result(400, "Already exists");
            }
            else
            {
                DB.UserClaims.Add(new IdentityUserClaim<Guid>
                {
                    ClaimType = Constants.GroupEditPermission,
                    UserId = value.UserId,
                    ClaimValue = groupId
                });
                await DB.SaveChangesAsync(token);
                return Result(200, "Succeeded");
            }
        }

        [HttpPut("{groupId:(^[a-zA-Z0-9-_ ]{4,128}$)}/claim/{userId:Guid}")]
        public async Task<ApiResult> DeleteClaim(Guid userId, string groupId, CancellationToken token)
        {
            if (!await HasPermissionToGroupAsync(groupId, token))
            {
                return Result(401, "No permission");
            }
            else if (!await DB.UserClaims.AnyAsync(x => x.ClaimValue == groupId && x.ClaimType == Constants.GroupEditPermission && x.UserId == userId, token))
            {
                return Result(404, "Claim not found");
            }
            else if (userId == User.Current.Id)
            {
                return Result(400, "Cannot remove yourself");
            }
            else
            {
                await DB.UserClaims
                    .Where(x => x.ClaimValue == groupId && x.ClaimType == Constants.GroupEditPermission && x.UserId == userId)
                    .DeleteAsync(token);

                return Result(200, "Delete succeeded");
            }
        }
        #endregion

        #region Private Functions
        private Task<bool> IsMemberOfGroup(string groupId, Guid userId, CancellationToken token)
            => DB.GroupMembers.AnyAsync(x => x.GroupId == groupId && x.UserId == userId, token);

        private async Task<bool> HasPermissionToGroupAsync(string groupId, CancellationToken token = default(CancellationToken))
            => !(User.Current == null
               || !await User.Manager.IsInAnyRolesAsync(User.Current, Constants.MasterOrHigherRoles)
               && !await DB.UserClaims.AnyAsync(x => x.UserId == User.Current.Id
                   && x.ClaimType == Constants.GroupEditPermission
                   && x.ClaimValue == groupId));
        #endregion
    }
}