﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Paramore.Brighter
{
    public abstract class RelationDatabaseOutbox(
        string outboxTableName,
        IRelationDatabaseOutboxQueries queries,
        ILogger logger)
        : IAmAnOutboxSync<Message, DbTransaction>, IAmAnOutboxAsync<Message, DbTransaction>
    {
        /// <summary>
        ///     If false we the default thread synchronization context to run any continuation, if true we re-use the original
        ///     synchronization context.
        ///     Default to false unless you know that you need true, as you risk deadlocks with the originating thread if you Wait
        ///     or access the Result or otherwise block. You may need the originating synchronization context if you need to access
        ///     thread specific storage such as HTTPContext
        /// </summary>
        public bool ContinueOnCapturedContext { get; set; }

        /// <summary>
        ///     Adds the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="outBoxTimeout"></param>
        /// <param name="transactionProvider">Connection Provider to use for this call</param>
        /// <returns>Task.</returns>
        public void Add(
            Message message, 
            int outBoxTimeout = -1,
            IAmABoxTransactionProvider<DbTransaction> transactionProvider = null)
        {
            var parameters = InitAddDbParameters(message);
            WriteToStore(transactionProvider, connection => InitAddDbCommand(connection, parameters), () =>
            {
                logger.LogWarning(
                    "MsSqlOutbox: A duplicate Message with the MessageId {Id} was inserted into the Outbox, ignoring and continuing",
                    message.Id);
            });
        }

        /// <summary>
        /// Adds the specified message.
        /// </summary>
        /// <param name="messages">The message.</param>
        /// <param name="outBoxTimeout"></param>
        /// <param name="transactionProvider">Connection Provider to use for this call</param>
        /// <returns>Task.</returns>
        public void Add(
            IEnumerable<Message> messages, 
            int outBoxTimeout = -1,
            IAmABoxTransactionProvider<DbTransaction> transactionProvider = null
            )
        {
            WriteToStore(transactionProvider,
                connection => InitBulkAddDbCommand(messages.ToList(), connection),
                () => logger.LogWarning("MsSqlOutbox: At least one message already exists in the outbox"));
        }

        /// <summary>
        /// Adds the specified message to the outbox 
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="outBoxTimeout"></param>
        /// <param name="transactionProvider">Connection Provider to use for this call</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Task&lt;Message&gt;.</returns>
        public Task AddAsync(Message message,
            int outBoxTimeout = -1,
            IAmABoxTransactionProvider<DbTransaction> transactionProvider = null,
            CancellationToken cancellationToken = default)
        {
            var parameters = InitAddDbParameters(message);
            return WriteToStoreAsync(transactionProvider,
                connection => InitAddDbCommand(connection, parameters), () =>
                {
                    logger.LogWarning(
                        "MsSqlOutbox: A duplicate Message with the MessageId {Id} was inserted into the Outbox, ignoring and continuing",
                        message.Id);
                },
                cancellationToken);
        }

        /// <summary>
        /// Awaitable add the specified message.
        /// </summary>
        /// <param name="messages">The message.</param>
        /// <param name="outBoxTimeout">The time allowed for the write in milliseconds; on a -1 default</param>
        /// <param name="transactionProvider">The Connection Provider to use for this call</param>
        /// <param name="cancellationToken">Allows the sender to cancel the request pipeline. Optional</param>
        /// <returns><see cref="Task"/>.</returns>
        public Task AddAsync(IEnumerable<Message> messages,
            int outBoxTimeout = -1,
            IAmABoxTransactionProvider<DbTransaction> transactionProvider = null,
            CancellationToken cancellationToken = default)
        {
            return WriteToStoreAsync(transactionProvider,
                connection => InitBulkAddDbCommand(messages.ToList(), connection),
                () => logger.LogWarning("MsSqlOutbox: At least one message already exists in the outbox"),
                cancellationToken);
        }

        /// <summary>
        /// Delete the specified messages
        /// </summary>
        /// <param name="messageIds">The id of the message to delete</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        public void Delete(string[] messageIds, Dictionary<string, object> args = null)
        {
            if(messageIds.Any())
                WriteToStore(null, connection => InitDeleteDispatchedCommand(connection, messageIds), null);
        }

        /// <summary>
        /// Delete the specified messages
        /// </summary>
        /// <param name="messageIds">The id of the message to delete</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        public Task DeleteAsync(string[] messageIds, Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            if(!messageIds.Any())
                return Task.CompletedTask;
            
            return WriteToStoreAsync(null, connection => InitDeleteDispatchedCommand(connection, messageIds), null,
                cancellationToken);
        }
        
        /// <summary>
        /// Retrieves messages that have been sent within the window
        /// </summary>
        /// <param name="millisecondsDispatchedSince">How long ago would the message have been dispatched in milliseconds</param>
        /// <param name="pageSize">How many messages in a page</param>
        /// <param name="pageNumber">Which page of messages to get</param>
        /// <param name="outboxTimeout"></param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        /// <returns>A list of dispatched messages</returns>
        public Task<IEnumerable<Message>> DispatchedMessagesAsync(
            double millisecondsDispatchedSince,
            int pageSize = 100, 
            int pageNumber = 1,
            int outboxTimeout = -1, 
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return ReadFromStoreAsync(
                connection =>
                    CreatePagedDispatchedCommand(connection, millisecondsDispatchedSince, pageSize, pageNumber),
                dr => MapListFunctionAsync(dr, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Retrieves messages that have been sent within the window
        /// </summary>
        /// <param name="millisecondsDispatchedSince">How long ago would the message have been dispatched in milliseconds</param>
        /// <param name="pageSize">How many messages in a page</param>
        /// <param name="pageNumber">Which page of messages to get</param>
        /// <param name="outboxTimeout"></param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <returns>A list of dispatched messages</returns>
        public IEnumerable<Message> DispatchedMessages(
            double millisecondsDispatchedSince,
            int pageSize = 100,
            int pageNumber = 1,
            int outboxTimeout = -1,
            Dictionary<string, object> args = null)
        {
            return ReadFromStore(
                connection =>
                    CreatePagedDispatchedCommand(connection, millisecondsDispatchedSince, pageSize, pageNumber),
                dr => MapListFunction(dr));
        }
        
        /// <summary>
        /// Get the messages that have been dispatched
        /// </summary>
        /// <param name="hoursDispatchedSince">The number of hours since the message was dispatched</param>
        /// <param name="pageSize">The amount to return</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <param name="cancellationToken">The Cancellation Token</param>
        /// <returns>Messages that have already been dispatched</returns>
        public Task<IEnumerable<Message>> DispatchedMessagesAsync(
            int hoursDispatchedSince, 
            int pageSize = 100,
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return ReadFromStoreAsync(connection => CreateDispatchedCommand(connection, hoursDispatchedSince, pageSize),
                dr => MapListFunctionAsync(dr, cancellationToken), cancellationToken);
        }


        /// <summary>
        /// Gets the specified message
        /// </summary>
        /// <param name="messageId">The id of the message to get</param>
        /// <param name="outBoxTimeout">How long to wait for the message before timing out</param>
        /// <param name="args">For outboxes that require additional parameters such as topic, provide an optional arg</param>
        /// <returns>The message</returns>
        public Message Get(string messageId, int outBoxTimeout = -1, Dictionary<string, object> args = null)
        {
            return ReadFromStore(connection => InitGetMessageCommand(connection, messageId, outBoxTimeout),
                dr => MapFunction(dr));
        }

        /// <summary>
        /// get as an asynchronous operation.
        /// </summary>
        /// <param name="messageId">The message identifier.</param>
        /// <param name="outBoxTimeout">The time allowed for the read in milliseconds; on  a -2 default</param>
        /// <param name="args">For outboxes that require additional parameters such as topic, provide an optional arg</param>
        /// <param name="cancellationToken">Allows the sender to cancel the request pipeline. Optional</param>
        /// <returns><see cref="Task{Message}" />.</returns>
        public Task<Message> GetAsync(
            string messageId,
            int outBoxTimeout = -1,
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return ReadFromStoreAsync(connection => InitGetMessageCommand(connection, messageId, outBoxTimeout),
                dr => MapFunctionAsync(dr, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Returns messages specified by the Ids
        /// </summary>
        /// <param name="outBoxTimeout">The Timeout of the outbox.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        /// <param name="messageIds">The Ids of the messages</param>
        /// <returns></returns>
        public Task<IEnumerable<Message>> GetAsync(
            IEnumerable<string> messageIds, 
            int outBoxTimeout = -1,
            CancellationToken cancellationToken = default
            )
        {
            return ReadFromStoreAsync(
                connection => InitGetMessagesCommand(connection, messageIds.ToList(), outBoxTimeout),
                async (dr) => await MapListFunctionAsync(dr, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Returns all messages in the store
        /// </summary>
        /// <param name="pageSize">Number of messages to return in search results (default = 100)</param>
        /// <param name="pageNumber">Page number of results to return (default = 1)</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <returns>A list of messages</returns>
        public IList<Message> Get(int pageSize = 100, int pageNumber = 1, Dictionary<string, object> args = null)
        {
            return ReadFromStore(connection => CreatePagedReadCommand(connection, pageSize, pageNumber),
                dr => MapListFunction(dr)).ToList();
        }

        /// <summary>
        /// Returns all messages in the store
        /// </summary>
        /// <param name="pageSize">Number of messages to return in search results (default = 100)</param>
        /// <param name="pageNumber">Page number of results to return (default = 1)</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns></returns>
        public async Task<IList<Message>> GetAsync(
            int pageSize = 100,
            int pageNumber = 1,
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return (await ReadFromStoreAsync(connection => CreatePagedReadCommand(connection, pageSize, pageNumber),
                dr => MapListFunctionAsync(dr, cancellationToken), cancellationToken)).ToList();
        }

        /// <summary>
        /// Get the number of messages in the Outbox that are not dispatched
        /// </summary>
        /// <param name="cancellationToken">Cancel the async operation</param>
        /// <returns></returns>
        public Task<int> GetNumberOfOutstandingMessagesAsync(CancellationToken cancellationToken)
        {
            return ReadFromStoreAsync(
                connection => CreateRemainingOutstandingCommand(connection),
                dr => MapOutstandingCountAsync(dr, cancellationToken), cancellationToken);
        }


        /// <summary>
        /// Update a message to show it is dispatched
        /// </summary>
        /// <param name="id">The id of the message to update</param>
        /// <param name="dispatchedAt">When was the message dispatched, defaults to UTC now</param>
        /// <param name="args">Allows additional arguments for specific Outbox Db providers</param>
        /// <param name="cancellationToken">Allows the sender to cancel the request pipeline. Optional</param>
        public Task MarkDispatchedAsync(
            string id, 
            DateTime? dispatchedAt = null, 
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return WriteToStoreAsync(null,
                connection => InitMarkDispatchedCommand(connection, id, dispatchedAt ?? DateTime.UtcNow), null,
                cancellationToken);
        }

        /// <summary>
        /// Update messages to show it is dispatched
        /// </summary>
        /// <param name="ids">The ids of the messages to update</param>
        /// <param name="dispatchedAt">When was the message dispatched, defaults to UTC now</param>
        /// <param name="args">Allows additional arguments to be passed for specific Db providers</param>
        /// <param name="cancellationToken">Allows the sender to cancel the request pipeline. Optional</param>
        public Task MarkDispatchedAsync(IEnumerable<string> ids,
            DateTime? dispatchedAt = null,
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return WriteToStoreAsync(null,
                connection => InitMarkDispatchedCommand(connection, ids, dispatchedAt ?? DateTime.UtcNow), null,
                cancellationToken);
        }

        /// <summary>
        /// Update a message to show it is dispatched
        /// </summary>
        /// <param name="id">The id of the message to update</param>
        /// <param name="dispatchedAt">When was the message dispatched, defaults to UTC now</param>
        /// <param name="args">Allows additional arguments to be provided for specific Outbox Db providers</param>
        public void MarkDispatched(string id, DateTime? dispatchedAt = null, Dictionary<string, object> args = null)
        {
            WriteToStore(null, connection => InitMarkDispatchedCommand(connection, id, dispatchedAt ?? DateTime.UtcNow),
                null);
        }

        /// <summary>
        /// Messages still outstanding in the Outbox because their timestamp
        /// </summary>
        /// <param name="millSecondsSinceSent">How many seconds since the message was sent do we wait to declare it outstanding</param>
        /// <param name="pageSize">The number of entries on a page</param>
        /// <param name="pageNumber">The page to return</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <returns>Outstanding Messages</returns>
        public IEnumerable<Message> OutstandingMessages(
            double millSecondsSinceSent,
            int pageSize = 100,
            int pageNumber = 1,
            Dictionary<string, object> args = null)
        {
            return ReadFromStore(
                connection => CreatePagedOutstandingCommand(connection, millSecondsSinceSent, pageSize, pageNumber),
                dr => MapListFunction(dr));
        }

        /// <summary>
        /// Messages still outstanding in the Outbox because their timestamp
        /// </summary>
        /// <param name="millSecondsSinceSent">How many seconds since the message was sent do we wait to declare it outstanding</param>
        /// <param name="pageSize">The number of entries to return in a page</param>
        /// <param name="pageNumber">The page number to return</param>
        /// <param name="args">Additional parameters required for search, if any</param>
        /// <param name="cancellationToken">Async Cancellation Token</param>
        /// <returns>Outstanding Messages</returns>
        public Task<IEnumerable<Message>> OutstandingMessagesAsync(
            double millSecondsSinceSent,
            int pageSize = 100,
            int pageNumber = 1,
            Dictionary<string, object> args = null,
            CancellationToken cancellationToken = default)
        {
            return ReadFromStoreAsync(
                connection => CreatePagedOutstandingCommand(connection, millSecondsSinceSent, pageSize, pageNumber),
                dr => MapListFunctionAsync(dr, cancellationToken), cancellationToken);
        }

        protected abstract void WriteToStore(
            IAmABoxTransactionProvider<DbTransaction> transactionProvider,
            Func<DbConnection, DbCommand> commandFunc, 
            Action loggingAction
            );

        protected abstract Task WriteToStoreAsync(
            IAmABoxTransactionProvider<DbTransaction> transactionProvider,
            Func<DbConnection, DbCommand> commandFunc, 
            Action loggingAction, 
            CancellationToken cancellationToken
            );

        protected abstract T ReadFromStore<T>(
            Func<DbConnection, DbCommand> commandFunc,
            Func<DbDataReader, T> resultFunc
            );

        protected abstract Task<T> ReadFromStoreAsync<T>(
            Func<DbConnection, DbCommand> commandFunc,
            Func<DbDataReader, Task<T>> resultFunc, 
            CancellationToken cancellationToken
            );

        private DbCommand CreatePagedDispatchedCommand(
            DbConnection connection, 
            double millisecondsDispatchedSince,
            int pageSize, 
            int pageNumber)
            => CreateCommand(connection, GenerateSqlText(queries.PagedDispatchedCommand), 0,
                CreateSqlParameter("PageNumber", pageNumber), CreateSqlParameter("PageSize", pageSize),
                CreateSqlParameter("OutstandingSince", -1 * millisecondsDispatchedSince));
        
        private DbCommand CreateDispatchedCommand(DbConnection connection, int hoursDispatchedSince,
            int pageSize)
            => CreateCommand(connection, GenerateSqlText(queries.DispatchedCommand), 0,
                CreateSqlParameter("PageSize", pageSize),
                CreateSqlParameter("DispatchedSince", -1 * hoursDispatchedSince));

        private DbCommand CreatePagedReadCommand(
            DbConnection connection, 
            int pageSize, 
            int pageNumber
            )
            => CreateCommand(connection, GenerateSqlText(queries.PagedReadCommand), 0,
                CreateSqlParameter("PageNumber", pageNumber), CreateSqlParameter("PageSize", pageSize));

        private DbCommand CreatePagedOutstandingCommand(
            DbConnection connection, 
            double milliSecondsSinceAdded,
            int pageSize, 
            int pageNumber)
            => CreateCommand(connection, GenerateSqlText(queries.PagedOutstandingCommand), 0,
                CreatePagedOutstandingParameters(milliSecondsSinceAdded, pageSize, pageNumber));
        
        private DbCommand CreateRemainingOutstandingCommand(DbConnection connection)
            => CreateCommand(connection, GenerateSqlText(queries.GetNumberOfOutstandingMessagesCommand), 0);

        private DbCommand InitAddDbCommand(
            DbConnection connection, 
            IDbDataParameter[] parameters
            )
            => CreateCommand(connection, GenerateSqlText(queries.AddCommand), 0, parameters);

        private DbCommand InitBulkAddDbCommand(List<Message> messages, DbConnection connection)
        {
            var insertClause = GenerateBulkInsert(messages);
            return CreateCommand(connection, GenerateSqlText(queries.BulkAddCommand, insertClause.insertClause), 0,
                insertClause.parameters);
        }

        private DbCommand InitMarkDispatchedCommand(DbConnection connection, string messageId, DateTime? dispatchedAt)
            => CreateCommand(connection, GenerateSqlText(queries.MarkDispatchedCommand), 0,
                CreateSqlParameter("MessageId", messageId),
                CreateSqlParameter("DispatchedAt", dispatchedAt?.ToUniversalTime()));

        private DbCommand InitMarkDispatchedCommand(DbConnection connection, IEnumerable<string> messageIds,
            DateTime? dispatchedAt)
        {
            var inClause = GenerateInClauseAndAddParameters(messageIds.ToList());
            return CreateCommand(connection, GenerateSqlText(queries.MarkMultipleDispatchedCommand, inClause.inClause), 0,
                inClause.parameters.Append(CreateSqlParameter("DispatchedAt", dispatchedAt?.ToUniversalTime()))
                    .ToArray());
        }

        private DbCommand InitGetMessageCommand(DbConnection connection, string messageId, int outBoxTimeout = -1)
            => CreateCommand(connection, GenerateSqlText(queries.GetMessageCommand), outBoxTimeout,
                CreateSqlParameter("MessageId", messageId));

        private DbCommand InitGetMessagesCommand(DbConnection connection, List<string> messageIds, int outBoxTimeout = -1)
        {
            var inClause = GenerateInClauseAndAddParameters(messageIds);
            return CreateCommand(connection, GenerateSqlText(queries.GetMessagesCommand, inClause.inClause), outBoxTimeout,
                inClause.parameters);
        }

        private string GenerateSqlText(string sqlFormat, params string[] orderedParams)
            => string.Format(sqlFormat, orderedParams.Prepend(outboxTableName).ToArray());

        private DbCommand InitDeleteDispatchedCommand(DbConnection connection, IEnumerable<string> messageIds)
        {
            var inClause = GenerateInClauseAndAddParameters(messageIds.ToList());
            return CreateCommand(connection, GenerateSqlText(queries.DeleteMessagesCommand, inClause.inClause), 0,
                inClause.parameters);
        }

        protected abstract DbCommand CreateCommand(DbConnection connection, string sqlText, int outBoxTimeout,
            params IDbDataParameter[] parameters);


        protected abstract IDbDataParameter[] CreatePagedOutstandingParameters(double milliSecondsSinceAdded,
            int pageSize, int pageNumber);      

        protected abstract IDbDataParameter CreateSqlParameter(string parameterName, object value);
        protected abstract IDbDataParameter[] InitAddDbParameters(Message message, int? position = null);

        protected abstract Message MapFunction(DbDataReader dr);

        protected abstract Task<Message> MapFunctionAsync(DbDataReader dr, CancellationToken cancellationToken);

        protected abstract IEnumerable<Message> MapListFunction(DbDataReader dr);

        protected abstract Task<IEnumerable<Message>> MapListFunctionAsync(DbDataReader dr, CancellationToken cancellationToken);
        
        protected abstract Task<int> MapOutstandingCountAsync(DbDataReader dr, CancellationToken cancellationToken);
        
        private (string inClause, IDbDataParameter[] parameters) GenerateInClauseAndAddParameters(List<string> messageIds)
        {
            var paramNames = messageIds.Select((s, i) => "@p" + i).ToArray();

            var parameters = new IDbDataParameter[messageIds.Count];
            for (int i = 0; i < paramNames.Count(); i++)
            {
                parameters[i] = CreateSqlParameter(paramNames[i], messageIds[i]);
            }

            return (string.Join(",", paramNames), parameters);
        }

        private  (string insertClause, IDbDataParameter[] parameters) GenerateBulkInsert(List<Message> messages)
        {
            var messageParams = new List<string>();
            var parameters = new List<IDbDataParameter>();

            for (int i = 0; i < messages.Count(); i++)
            {
                messageParams.Add($"(@p{i}_MessageId, @p{i}_MessageType, @p{i}_Topic, @p{i}_Timestamp, @p{i}_CorrelationId, @p{i}_ReplyTo, @p{i}_ContentType, @p{i}_PartitionKey, @p{i}_HeaderBag, @p{i}_Body)");
                parameters.AddRange(InitAddDbParameters(messages[i], i));
            }

            return (string.Join(",", messageParams), parameters.ToArray());
        }
    }
}
